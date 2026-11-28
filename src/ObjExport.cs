using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Road96Toolkit
{
    // Saves objects as Wavefront .obj with a .mtl and the base colour textures as PNG, so they open
    // in Blender or any 3D program (File > Import > Wavefront).
    //
    // The game locks most of its meshes in memory, so those are read from the game's files instead
    // (see DiskMeshes). Characters are posed as they stand right now: their skin is moved with their
    // own skeleton. Level geometry that Unity merged into one big mesh (static batching) is cut back
    // out by keeping the parts of the merged mesh that sit inside the object.
    internal static class ObjExport
    {
        public static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "Road96Toolkit", "exported", "models");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class Part
        {
            public string Name;
            public Vector3[] Verts, Normals;
            public Vector2[] Uvs;
            public List<int[]> Subs = new List<int[]>();
            public List<Material> Mats = new List<Material>();
            public Matrix4x4 ToRoot;
        }

        class Job
        {
            public Matrix4x4 ToRoot;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<string> Locked = new List<string>();
            public readonly HashSet<string> Done = new HashSet<string>();
            public int FromDisk;
        }

        public static string LastResult { get; private set; }

        public static void Export(GameObject go)
        {
            if (go == null) return;
            var name = go.name.Replace(" (toolkit)", "").Trim();
            Run(name, go.transform.worldToLocalMatrix, go.GetComponentsInChildren<Renderer>(true).ToList());
        }

        // everything around a point, as one file with the point in the middle
        public static void ExportArea(Vector3 center, float radius)
        {
            var list = new List<Renderer>();
            Game.Try("Find area", () =>
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.TryCast<ParticleSystemRenderer>() != null) continue;
                    if (r.bounds.SqrDistance(center) > radius * radius) continue;
                    list.Add(r);
                }
            });
            Run($"area {TravelTab.SceneName} {(int)radius}m {DateTime.Now:HHmmss}", Matrix4x4.Translate(-center), list);
        }

        // a mesh asset on its own
        public static bool ExportMesh(Mesh mesh, IList<Material> mats, bool quiet = false)
        {
            if (mesh == null) return false;
            bool ok = false;
            Game.Try("Export mesh", () =>
            {
                var job = new Job { ToRoot = Matrix4x4.identity };
                var g = Geo(mesh, -1, job, mesh.name);
                if (g != null) job.Parts.Add(ToPart(mesh.name, g, mats, Matrix4x4.identity, null));
                ok = Save(mesh.name, job, quiet);
            });
            return ok;
        }

        static void Run(string name, IList<Renderer> renderers) => Run(name, Matrix4x4.identity, renderers);

        static void Run(string name, Matrix4x4 toRoot, IList<Renderer> renderers)
        {
            Game.Try("Export OBJ", () =>
            {
                var job = new Job { ToRoot = toRoot };
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    try { Collect(r, job); }
                    catch (Exception e) { job.Locked.Add(r.name + " (" + e.GetType().Name + ": " + e.Message + ")"); }
                }
                Save(name, job, false);
            });
        }

        static bool Save(string rawName, Job job, bool quiet)
        {
            var name = SceneEditor.Safe(rawName);
            if (job.Parts.Count == 0)
            {
                LastResult = job.Locked.Count > 0 ? $"Could not read any of the {job.Locked.Count} meshes, see the log" : "Nothing with a mesh in it";
                if (!DiskMeshes.Ready) LastResult += ". The game files are still being read, try again in a moment";
                if (!quiet) ToolkitBehaviour.Toast(LastResult);
                foreach (var l in job.Locked) Plugin.Log.LogInfo("OBJ export, skipped: " + l);
                return false;
            }
            var dir = Path.Combine(Folder, name);
            Directory.CreateDirectory(dir);
            int tex = Write(dir, name, job.Parts);
            LastResult = $"Saved {job.Parts.Count} meshes and {tex} textures to exported/models/{name}"
                + (job.FromDisk > 0 ? $" ({job.FromDisk} read from the game files)" : "")
                + (job.Locked.Count > 0 ? $", {job.Locked.Count} skipped" : "");
            if (!quiet) ToolkitBehaviour.Toast(LastResult);
            Plugin.Log.LogInfo($"OBJ export: {dir}  parts {job.Parts.Count}  textures {tex}  from disk {job.FromDisk}");
            foreach (var l in job.Locked) Plugin.Log.LogInfo("OBJ export, skipped: " + l);
            return true;
        }

        static int SceneIndex(Component c)
        {
            try { return c.gameObject.scene.IsValid() ? c.gameObject.scene.buildIndex : -1; } catch { return -1; }
        }

        // the shape of a mesh: from memory when the game allows it, else from the game files
        static DiskMeshes.Data Geo(Mesh mesh, int buildIndex, Job job, string who)
        {
            if (mesh.isReadable)
            {
                var d = new DiskMeshes.Data { Name = mesh.name, VertexCount = mesh.vertexCount, V = mesh.vertices.ToArray() };
                var n = mesh.normals; if (n != null && n.Length == d.V.Length) d.N = n.ToArray();
                var uv = mesh.uv; if (uv != null && uv.Length == d.V.Length) d.Uv = uv.ToArray();
                for (int i = 0; i < mesh.subMeshCount; i++) d.Subs.Add(mesh.GetTriangles(i).ToArray());
                return d;
            }
            var disk = DiskMeshes.Get(mesh, buildIndex);
            if (disk == null)
            {
                job.Locked.Add(who + " (" + mesh.name + (DiskMeshes.Ready ? ", not found in the game files)" : ", game files not read yet)"));
                return null;
            }
            job.FromDisk++;
            return disk;
        }

        static Part ToPart(string name, DiskMeshes.Data g, IList<Material> mats, Matrix4x4 m, IList<int> onlySubs)
        {
            var p = new Part { Name = name, ToRoot = m, Verts = g.V, Normals = g.N, Uvs = g.Uv };
            int mi = 0;
            for (int i = 0; i < g.Subs.Count; i++)
            {
                if (onlySubs != null && !onlySubs.Contains(i)) continue;
                p.Subs.Add(g.Subs[i]);
                int slot = onlySubs != null ? mi++ : i;
                p.Mats.Add(mats != null && mats.Count > 0 ? mats[Math.Min(slot, mats.Count - 1)] : null);
            }
            return p;
        }

        static void Collect(Renderer r, Job job)
        {
            var smr = r.TryCast<SkinnedMeshRenderer>();
            if (smr != null) { AddSkinned(smr, job); return; }
            var mr = r.TryCast<MeshRenderer>();
            if (mr == null) return;
            var mf = mr.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return;
            if (mr.isPartOfStaticBatch) { AddBatched(mr, mesh, job); return; }
            var g = Geo(mesh, SceneIndex(mr), job, mr.name);
            if (g == null) return;
            job.Parts.Add(ToPart(mr.name, g, mr.sharedMaterials, job.ToRoot * mr.transform.localToWorldMatrix, null));
        }

        static void AddSkinned(SkinnedMeshRenderer smr, Job job)
        {
            var src = smr.sharedMesh;
            if (src == null) return;
            if (src.isReadable)
            {
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked);
                    // the baked mesh already holds the renderer's scale, so only move and turn it
                    var t = smr.transform;
                    var g = Geo(baked, -1, job, smr.name);
                    if (g != null) job.Parts.Add(ToPart(smr.name, g, smr.sharedMaterials, job.ToRoot * Matrix4x4.TRS(t.position, t.rotation, Vector3.one), null));
                }
                finally { UnityEngine.Object.Destroy(baked); }
                return;
            }
            var d = Geo(src, SceneIndex(smr), job, smr.name);
            if (d == null) return;
            var bones = smr.bones;
            if (d.Bi == null || bones == null || bones.Length == 0 || d.Bind == null || d.Bind.Length == 0)
            {
                job.Parts.Add(ToPart(smr.name, d, smr.sharedMaterials, job.ToRoot * smr.transform.localToWorldMatrix, null));
                return;
            }
            // pose it like it stands now: every vertex follows its bones
            int nb = Math.Min(bones.Length, d.Bind.Length);
            var mats = new Matrix4x4[nb];
            var fallback = smr.transform.localToWorldMatrix;
            for (int i = 0; i < nb; i++) mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * d.Bind[i] : fallback;
            var v = new Vector3[d.V.Length];
            var n = d.N != null ? new Vector3[d.N.Length] : null;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 pv = Vector3.zero, pn = Vector3.zero;
                float sum = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float w = d.W[i * 4 + k];
                    int b = d.Bi[i * 4 + k];
                    if (w <= 0f || b < 0 || b >= nb) continue;
                    pv += mats[b].MultiplyPoint3x4(d.V[i]) * w;
                    if (n != null) pn += mats[b].MultiplyVector(d.N[i]) * w;
                    sum += w;
                }
                if (sum <= 0f) { pv = fallback.MultiplyPoint3x4(d.V[i]); if (n != null) pn = fallback.MultiplyVector(d.N[i]); }
                else if (sum < 0.999f) pv /= sum;
                v[i] = pv;
                if (n != null) n[i] = pn;
            }
            var posed = new DiskMeshes.Data { Name = d.Name, VertexCount = d.VertexCount, V = v, N = n, Uv = d.Uv, Subs = d.Subs };
            job.Parts.Add(ToPart(smr.name, posed, smr.sharedMaterials, job.ToRoot, null));
        }

        // merged level meshes: keep the sub meshes that sit inside this renderer
        static void AddBatched(MeshRenderer mr, Mesh mesh, Job job)
        {
            var g = Geo(mesh, SceneIndex(mr), job, mr.name + " (merged level mesh)");
            if (g == null) return;
            var bounds = mr.bounds;
            bounds.Expand(0.02f);
            var batchRoot = mr.staticBatchRootTransform;
            var toWorld = batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity;
            var keep = new List<int>();
            for (int i = 0; i < g.Subs.Count; i++)
            {
                var tri = g.Subs[i];
                if (tri.Length == 0) continue;
                string key = mesh.Pointer.ToInt64() + "/" + i;
                if (job.Done.Contains(key)) continue;
                bool inside = true;
                foreach (var k in tri)
                    if (!bounds.Contains(toWorld.MultiplyPoint3x4(g.V[k]))) { inside = false; break; }
                if (!inside) continue;
                keep.Add(i);
                job.Done.Add(key);
            }
            if (keep.Count == 0) { job.Locked.Add(mr.name + " (could not find it in the merged level mesh)"); return; }
            job.Parts.Add(ToPart(mr.name, g, mr.sharedMaterials, job.ToRoot * toWorld, keep));
        }

        static string F(float v) => v.ToString("0.######", Inv);

        static int Write(string dir, string name, List<Part> parts)
        {
            var obj = new StringBuilder();
            var mtl = new StringBuilder();
            obj.AppendLine("# Road 96, " + name);
            obj.AppendLine("mtllib " + name + ".mtl");
            var matNames = new Dictionary<IntPtr, string>();
            int texCount = 0, baseV = 1;

            foreach (var p in parts)
            {
                obj.AppendLine("o " + Clean(p.Name));
                var nm = p.ToRoot.inverse.transpose;
                // only the vertices the faces use (a merged level mesh holds far more)
                var remap = new int[p.Verts.Length];
                for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                var used = new List<int>();
                foreach (var tri0 in p.Subs) foreach (var k in tri0) if (remap[k] < 0) { remap[k] = used.Count; used.Add(k); }
                bool hasN = p.Normals != null && p.Normals.Length == p.Verts.Length;
                bool hasT = p.Uvs != null && p.Uvs.Length == p.Verts.Length;
                // Unity is left handed, OBJ is right handed: flip X and the face order
                foreach (var k in used)
                {
                    var w = p.ToRoot.MultiplyPoint3x4(p.Verts[k]);
                    obj.Append("v ").Append(F(-w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).AppendLine();
                }
                if (hasT) foreach (var k in used) obj.Append("vt ").Append(F(p.Uvs[k].x)).Append(' ').Append(F(p.Uvs[k].y)).AppendLine();
                if (hasN)
                    foreach (var k in used)
                    {
                        var w = nm.MultiplyVector(p.Normals[k]).normalized;
                        obj.Append("vn ").Append(F(-w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).AppendLine();
                    }

                for (int s = 0; s < p.Subs.Count; s++)
                {
                    var mat = p.Mats[s];
                    string mn = "none";
                    if (mat != null)
                    {
                        if (!matNames.TryGetValue(mat.Pointer, out mn))
                        {
                            mn = Clean(mat.name) + "_" + matNames.Count;
                            matNames[mat.Pointer] = mn;
                            texCount += WriteMaterial(dir, mn, mat, mtl);
                        }
                    }
                    obj.AppendLine("usemtl " + mn);
                    var tri = p.Subs[s];
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        obj.Append('f');
                        foreach (var k in new[] { tri[i + 2], tri[i + 1], tri[i] })
                        {
                            int idx = remap[k] + baseV;
                            obj.Append(' ').Append(idx);
                            if (hasT || hasN) obj.Append('/').Append(hasT ? idx.ToString(Inv) : "");
                            if (hasN) obj.Append('/').Append(idx);
                        }
                        obj.AppendLine();
                    }
                }
                baseV += used.Count;
            }
            File.WriteAllText(Path.Combine(dir, name + ".obj"), obj.ToString());
            File.WriteAllText(Path.Combine(dir, name + ".mtl"), mtl.ToString());
            return texCount;
        }

        static readonly string[] ColorMaps = { "_BaseColorMap", "_MainTex", "_BaseMap", "_UnlitColorMap", "_Albedo" };
        static readonly string[] Colors = { "_BaseColor", "_Color", "_UnlitColor" };

        static int WriteMaterial(string dir, string mn, Material mat, StringBuilder mtl)
        {
            mtl.AppendLine("newmtl " + mn);
            var c = Color.white;
            foreach (var k in Colors) if (mat.HasProperty(k)) { c = mat.GetColor(k); break; }
            mtl.AppendLine($"Kd {F(c.r)} {F(c.g)} {F(c.b)}");
            if (c.a < 0.999f) mtl.AppendLine("d " + F(c.a));
            Texture tex = null;
            foreach (var k in ColorMaps)
                if (mat.HasProperty(k)) { tex = mat.GetTexture(k); if (tex != null) break; }
            if (tex == null) { mtl.AppendLine(); return 0; }
            var file = Clean(tex.name) + ".png";
            var path = Path.Combine(dir, file);
            if (!File.Exists(path))
            {
                var png = Png(tex);
                if (png == null) { mtl.AppendLine(); return 0; }
                File.WriteAllBytes(path, png);
            }
            mtl.AppendLine("map_Kd " + file);
            mtl.AppendLine();
            return 1;
        }

        // any texture to PNG bytes, by drawing it into a texture we can read
        internal static byte[] Png(Texture src, Rect? uv = null, int maxSize = 4096)
        {
            if (src == null) return null;
            RenderTexture rt = null;
            var prev = RenderTexture.active;
            Texture2D t = null;
            try
            {
                var r = uv ?? new Rect(0, 0, 1, 1);
                int w = Math.Max(1, (int)(r.width * src.width)), h = Math.Max(1, (int)(r.height * src.height));
                float k = Math.Min(1f, (float)maxSize / Math.Max(w, h));
                w = Math.Max(1, (int)(w * k)); h = Math.Max(1, (int)(h * k));
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt, new Vector2(r.width, r.height), new Vector2(r.x, r.y));
                RenderTexture.active = rt;
                t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t.Apply();
                return ImageConversion.EncodeToPNG(t);
            }
            catch (Exception e) { Plugin.Log.LogWarning("PNG copy failed for " + src.name + ": " + e.Message); return null; }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (t != null) UnityEngine.Object.Destroy(t);
            }
        }

        internal static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var sb = new StringBuilder();
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' ? ch : '_');
            return sb.ToString();
        }
    }
}
