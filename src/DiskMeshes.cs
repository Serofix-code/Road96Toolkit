using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Road96Toolkit
{
    // The game ships its meshes locked: once a mesh is on the graphics card its vertices are gone from
    // memory and scripts can't read them. The data is still in the game's own files though
    // (Road 96_Data/levelN, sharedassetsN.assets, resources.assets and their .resS files), so this
    // reads the mesh from there instead. Unity 2020.3 serialized files, format 22, no type trees.
    internal static class DiskMeshes
    {
        public class Data
        {
            public string Name;
            public int VertexCount;
            public Vector3[] V, N;
            public Vector2[] Uv;
            public List<int[]> Subs = new List<int[]>();
            public float[] W;      // 4 weights per vertex, or null
            public int[] Bi;       // 4 bone indices per vertex, or null
            public Matrix4x4[] Bind;
        }

        struct Loc { public string File; public long Offset; public uint Size; }

        static readonly Dictionary<string, List<Loc>> _index = new Dictionary<string, List<Loc>>(StringComparer.Ordinal);
        static volatile int _state; // 0 not started, 1 building, 2 ready, 3 failed
        static int _files, _meshes;
        static string _error;
        static readonly Dictionary<IntPtr, Data> _cache = new Dictionary<IntPtr, Data>();

        static string DataDir => Path.Combine(BepInEx.Paths.GameRootPath, "Road 96_Data");

        public static bool Ready => _state == 2;
        public static string Status => _state switch
        {
            0 => "not read yet",
            1 => "reading the game files...",
            2 => $"{_meshes} meshes in {_files} game files",
            _ => "could not read the game files: " + _error,
        };

        public static void Start()
        {
            if (_state != 0) return;
            _state = 1;
            var th = new Thread(Build) { IsBackground = true, Name = "Toolkit mesh index" };
            th.Start();
        }

        static void Build()
        {
            try
            {
                foreach (var f in Directory.GetFiles(DataDir))
                {
                    var n = Path.GetFileName(f);
                    bool level = n.StartsWith("level") && !n.Contains(".");
                    bool shared = n.StartsWith("sharedassets") && n.EndsWith(".assets");
                    if (!level && !shared && n != "resources.assets") continue;
                    try { IndexFile(f); _files++; } catch { }
                }
                _state = 2;
            }
            catch (Exception e) { _error = e.Message; _state = 3; }
        }

        static uint BE(byte[] b, int p) => (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]);

        static void IndexFile(string file)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[48];
            if (fs.Read(head, 0, 48) < 48) return;
            if (BE(head, 8) < 22) return;
            long meta = BE(head, 20);
            long dataOff = (long)BE(head, 32) << 32 | BE(head, 36);
            var md = new byte[meta + 64];
            fs.Read(md, 0, md.Length);
            int p = 0;
            while (md[p] != 0) p++;
            p++;                     // unity version
            p += 4;                  // platform
            bool tree = md[p++] != 0;
            if (tree) return;
            int types = BitConverter.ToInt32(md, p); p += 4;
            var cls = new int[types];
            for (int i = 0; i < types; i++)
            {
                cls[i] = BitConverter.ToInt32(md, p); p += 4;
                p += 1 + 2;
                if (cls[i] == 114) p += 16;
                p += 16;
            }
            int count = BitConverter.ToInt32(md, p); p += 4;
            var found = new List<(long off, uint size)>();
            for (int i = 0; i < count; i++)
            {
                p = (p + 3) & ~3;
                p += 8;
                long start = BitConverter.ToInt64(md, p); p += 8;
                uint size = BitConverter.ToUInt32(md, p); p += 4;
                int tid = BitConverter.ToInt32(md, p); p += 4;
                if (tid >= 0 && tid < types && cls[tid] == 43) found.Add((dataOff + start, size));
            }
            var nb = new byte[260];
            foreach (var (off, size) in found)
            {
                fs.Seek(off, SeekOrigin.Begin);
                int got = fs.Read(nb, 0, nb.Length);
                if (got < 4) continue;
                int len = BitConverter.ToInt32(nb, 0);
                if (len <= 0 || len > got - 4) continue;
                var name = Encoding.UTF8.GetString(nb, 4, len);
                lock (_index)
                {
                    if (!_index.TryGetValue(name, out var l)) _index[name] = l = new List<Loc>();
                    l.Add(new Loc { File = file, Offset = off, Size = size });
                    _meshes++;
                }
            }
        }

        // the mesh as it is stored on disk; the scene's own file is tried first since
        // merged level meshes share their names between levels
        public static Data Get(Mesh live, int buildIndex = -1)
        {
            if (live == null) return null;
            if (_cache.TryGetValue(live.Pointer, out var c)) return c;
            if (!Ready) return null;
            List<Loc> locs;
            lock (_index) if (!_index.TryGetValue(live.name, out locs)) return null;
            int vc = live.vertexCount, sc = live.subMeshCount;
            string mine = buildIndex >= 0 ? "level" + buildIndex : null, shared = buildIndex >= 0 ? "sharedassets" + buildIndex + ".assets" : null;
            var order = new List<Loc>(locs);
            order.Sort((a, b) => Rank(a) - Rank(b));
            int Rank(Loc l) { var n = Path.GetFileName(l.File); return n == mine ? 0 : n == shared ? 1 : 2; }
            foreach (var l in order)
            {
                try
                {
                    var d = Read(l, vc, sc);
                    if (d == null) continue;
                    _cache[live.Pointer] = d;
                    return d;
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Mesh {live.name} from {Path.GetFileName(l.File)}: {e.Message}"); }
            }
            return null;
        }

        static readonly int[] FmtSize = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };

        class Rd
        {
            public byte[] B; public int P;
            public byte U8() => B[P++];
            public int I32() { int v = BitConverter.ToInt32(B, P); P += 4; return v; }
            public uint U32() { uint v = BitConverter.ToUInt32(B, P); P += 4; return v; }
            public float F() { float v = BitConverter.ToSingle(B, P); P += 4; return v; }
            public void Align() => P = (P + 3) & ~3;
            public string S() { int n = I32(); var s = Encoding.UTF8.GetString(B, P, n); P += n; Align(); return s; }
            public (int at, int len) Bytes() { int n = I32(); int at = P; P += n; Align(); return (at, n); }
            public void Skip(int n) => P += n;
            public void PackedFloat() { U32(); F(); F(); Bytes(); U8(); Align(); }
            public void PackedInt() { U32(); Bytes(); U8(); Align(); }
        }

        static Data Read(Loc l, int wantVerts, int wantSubs)
        {
            byte[] b;
            using (var fs = new FileStream(l.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(l.Offset, SeekOrigin.Begin);
                b = new byte[l.Size];
                fs.Read(b, 0, b.Length);
            }
            var r = new Rd { B = b };
            var d = new Data { Name = r.S() };
            int subs = r.I32();
            if (wantSubs > 0 && subs != wantSubs) return null;
            var sm = new (uint first, uint count, int topo, uint baseV)[subs];
            for (int i = 0; i < subs; i++)
            {
                sm[i] = (r.U32(), r.U32(), r.I32(), r.U32());
                r.U32(); r.U32();      // first vertex, vertex count
                r.Skip(24);            // bounds
            }
            r.Skip(40 * r.I32());                                   // blend shape vertices
            int shapes = r.I32();
            for (int i = 0; i < shapes; i++) { r.U32(); r.U32(); r.U8(); r.U8(); r.Align(); }
            int chs = r.I32();
            for (int i = 0; i < chs; i++) { r.S(); r.U32(); r.I32(); r.I32(); }
            r.Skip(4 * r.I32());                                    // full weights
            int binds = r.I32();
            d.Bind = new Matrix4x4[binds];
            for (int i = 0; i < binds; i++)
            {
                var m = new Matrix4x4();
                for (int k = 0; k < 16; k++) m[k / 4, k % 4] = r.F();   // stored row by row
                d.Bind[i] = m;
            }
            r.Skip(4 * r.I32());   // bone name hashes
            r.U32();               // root bone hash
            r.Skip(24 * r.I32());  // bone bounds
            r.Skip(4 * r.I32());   // variable bone weights
            int compression = r.U8(); r.U8(); r.U8(); r.U8(); r.Align();
            if (compression != 0) throw new Exception("compressed mesh, not supported");
            int indexFormat = r.I32();
            var (ibAt, ibLen) = r.Bytes();
            int vc = (int)r.U32();
            if (wantVerts > 0 && vc != wantVerts) return null;
            d.VertexCount = vc;
            int nch = r.I32();
            var ch = new (int stream, int off, int fmt, int dim)[nch];
            for (int i = 0; i < nch; i++) ch[i] = (r.U8(), r.U8(), r.U8(), r.U8() & 0xF);
            var (vdAt, vdLen) = r.Bytes();
            for (int i = 0; i < 4; i++) r.PackedFloat();
            r.PackedInt(); r.PackedInt(); r.PackedInt();
            r.PackedFloat();
            r.PackedInt(); r.PackedInt();
            r.U32();
            r.Skip(24); r.I32();
            r.Bytes(); r.Bytes();
            r.F(); r.F();
            ulong sOff = BitConverter.ToUInt64(b, r.P); r.P += 8;
            uint sSize = r.U32();
            string sPath = r.S();

            byte[] vb; int vbAt = 0;
            if (vdLen == 0 && sSize > 0)
            {
                var res = Path.Combine(Path.GetDirectoryName(l.File), Path.GetFileName(sPath));
                using var fs = new FileStream(res, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek((long)sOff, SeekOrigin.Begin);
                vb = new byte[sSize];
                fs.Read(vb, 0, vb.Length);
            }
            else { vb = b; vbAt = vdAt; }

            int streams = 0;
            foreach (var c in ch) if (c.dim > 0) streams = Math.Max(streams, c.stream + 1);
            var stride = new int[streams];
            foreach (var c in ch) if (c.dim > 0) stride[c.stream] += c.dim * FmtSize[c.fmt];
            var soff = new int[streams];
            int o = 0;
            for (int s = 0; s < streams; s++) { soff[s] = o; o += vc * stride[s]; o = (o + 15) & ~15; }

            float Val(int at, int fmt)
            {
                switch (fmt)
                {
                    case 0: return BitConverter.ToSingle(vb, at);
                    case 1: return (float)BitConverter.Int16BitsToHalf(BitConverter.ToInt16(vb, at));
                    case 2: return vb[at] / 255f;
                    case 3: return Math.Max(-1f, (sbyte)vb[at] / 127f);
                    case 4: return BitConverter.ToUInt16(vb, at) / 65535f;
                    case 5: return Math.Max(-1f, BitConverter.ToInt16(vb, at) / 32767f);
                    case 6: return vb[at];
                    case 7: return (sbyte)vb[at];
                    case 8: return BitConverter.ToUInt16(vb, at);
                    case 9: return BitConverter.ToInt16(vb, at);
                    case 10: return BitConverter.ToUInt32(vb, at);
                    default: return BitConverter.ToInt32(vb, at);
                }
            }
            float[] Channel(int c, out int dim)
            {
                dim = 0;
                if (c >= ch.Length || ch[c].dim == 0) return null;
                var x = ch[c];
                dim = x.dim;
                int sz = FmtSize[x.fmt];
                var res = new float[vc * dim];
                for (int i = 0; i < vc; i++)
                {
                    int at = vbAt + soff[x.stream] + i * stride[x.stream] + x.off;
                    for (int k = 0; k < dim; k++) res[i * dim + k] = Val(at + k * sz, x.fmt);
                }
                return res;
            }

            var pos = Channel(0, out int pd);
            if (pos == null || pd < 3) throw new Exception("no positions");
            d.V = new Vector3[vc];
            for (int i = 0; i < vc; i++) d.V[i] = new Vector3(pos[i * pd], pos[i * pd + 1], pos[i * pd + 2]);
            var nor = Channel(1, out int nd);
            if (nor != null && nd >= 3)
            {
                d.N = new Vector3[vc];
                for (int i = 0; i < vc; i++) d.N[i] = new Vector3(nor[i * nd], nor[i * nd + 1], nor[i * nd + 2]);
            }
            var uv = Channel(4, out int ud);
            if (uv != null && ud >= 2)
            {
                d.Uv = new Vector2[vc];
                for (int i = 0; i < vc; i++) d.Uv[i] = new Vector2(uv[i * ud], uv[i * ud + 1]);
            }
            var wt = Channel(12, out int wd);
            var bi = Channel(13, out int bd);
            if (bi != null)
            {
                d.W = new float[vc * 4];
                d.Bi = new int[vc * 4];
                for (int i = 0; i < vc; i++)
                    for (int k = 0; k < 4; k++)
                    {
                        d.Bi[i * 4 + k] = k < bd ? (int)bi[i * bd + k] : 0;
                        d.W[i * 4 + k] = wt != null && k < wd ? wt[i * wd + k] : (k == 0 && wt == null ? 1f : 0f);
                    }
            }

            for (int s = 0; s < subs; s++)
            {
                var (first, count, topo, baseV) = sm[s];
                var tri = new List<int>((int)count);
                int w = indexFormat == 0 ? 2 : 4;
                var idx = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int at = ibAt + (int)first + i * w;
                    idx[i] = (w == 2 ? BitConverter.ToUInt16(b, at) : (int)BitConverter.ToUInt32(b, at)) + (int)baseV;
                }
                if (topo == 0) tri.AddRange(idx);
                else if (topo == 2)           // quads
                    for (int i = 0; i + 3 < idx.Length; i += 4) { tri.Add(idx[i]); tri.Add(idx[i + 1]); tri.Add(idx[i + 2]); tri.Add(idx[i]); tri.Add(idx[i + 2]); tri.Add(idx[i + 3]); }
                else if (topo == 1)           // strips
                    for (int i = 0; i + 2 < idx.Length; i++)
                    {
                        if (idx[i] == idx[i + 1] || idx[i + 1] == idx[i + 2] || idx[i] == idx[i + 2]) continue;
                        if ((i & 1) == 0) { tri.Add(idx[i]); tri.Add(idx[i + 1]); tri.Add(idx[i + 2]); }
                        else { tri.Add(idx[i + 1]); tri.Add(idx[i]); tri.Add(idx[i + 2]); }
                    }
                d.Subs.Add(tri.ToArray());
            }
            return d;
        }
    }
}
