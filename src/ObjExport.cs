using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Road96Toolkit
{
    // Saves the selected object (and everything under it) as a Wavefront .obj with a .mtl and the
    // base colour textures as PNG, so it opens in Blender or any 3D program.
    //
    // Characters are posed as they are right now (their skinned meshes are baked first). Some meshes
    // are locked by the game (not readable from scripts); those are skipped and listed in the log.
    // Level geometry that Unity merged into one big mesh (static batching) is cut back out by finding
    // the parts of the merged mesh that sit inside the object.
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

        public static string LastResult { get; private set; }

        public static void Export(GameObject go)
        {
            if (go == null) return;
            Game.Try("Export OBJ", () =>
            {
                var root = go.transform;
                var name = SceneEditor.Safe(go.name.Replace(" (toolkit)", "").Trim());
                var dir = Path.Combine(Folder, name);
                Directory.CreateDirectory(dir);

                var parts = new List<Part>();
                var locked = new List<string>();
                var toRoot = root.worldToLocalMatrix;

                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var smr = r.TryCast<SkinnedMeshRenderer>();
                    if (smr != null) { AddSkinned(smr, toRoot, parts, locked); continue; }
                    var mr = r.TryCast<MeshRenderer>();
                    if (mr == null) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (mr.isPartOfStaticBatch) AddBatched(mr, mesh, toRoot, parts, locked);
                    else AddMesh(mr.name, mesh, mr.sharedMaterials, toRoot * mr.transform.localToWorldMatrix, parts, locked, mr.GetComponent<MeshCollider>());
                }

                if (parts.Count == 0)
                {
                    LastResult = locked.Count > 0 ? $"All {locked.Count} meshes are locked by the game, nothing to save" : "Nothing with a mesh in it";
                    ToolkitBehaviour.Toast(LastResult);
                    return;
                }

                int tex = Write(dir, name, parts);
                LastResult = $"Saved {parts.Count} meshes and {tex} textures to exported/models/{name}" + (locked.Count > 0 ? $", {locked.Count} locked" : "");
                ToolkitBehaviour.Toast(LastResult);
                Plugin.Log.LogInfo($"OBJ export: {dir}  parts {parts.Count}  textures {tex}");
                foreach (var l in locked) Plugin.Log.LogInfo("OBJ export, locked mesh skipped: " + l);
            });
        }

        static void AddSkinned(SkinnedMeshRenderer smr, Matrix4x4 toRoot, List<Part> parts, List<string> locked)
        {
            var src = smr.sharedMesh;
            if (src == null) return;
            if (!src.isReadable) { locked.Add(smr.name + " (" + src.name + ")"); return; }
            var baked = new Mesh();
            try
            {
                smr.BakeMesh(baked);
                // the baked mesh already holds the renderer's scale, so only move and turn it
                var t = smr.transform;
                var m = toRoot * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                AddMesh(smr.name, baked, smr.sharedMaterials, m, parts, locked, null);
            }
            finally { UnityEngine.Object.Destroy(baked); }
        }

        static void AddMesh(string name, Mesh mesh, IList<Material> mats, Matrix4x4 m, List<Part> parts, List<string> locked, MeshCollider col)
        {
            if (!mesh.isReadable)
            {
                // a collider often holds a readable copy of the same shape
                var cm = col != null ? col.sharedMesh : null;
                if (cm != null && cm.isReadable && cm.vertexCount == mesh.vertexCount) mesh = cm;
                else { locked.Add(name + " (" + mesh.name + ")"); return; }
            }
            var p = new Part { Name = name, ToRoot = m, Verts = mesh.vertices.ToArray(), Normals = mesh.normals?.ToArray(), Uvs = mesh.uv?.ToArray() };
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                p.Subs.Add(mesh.GetTriangles(i).ToArray());
                p.Mats.Add(mats != null && mats.Count > 0 ? mats[Math.Min(i, mats.Count - 1)] : null);
            }
            parts.Add(p);
        }

        // merged level meshes: keep the sub meshes that sit inside this renderer
        static void AddBatched(MeshRenderer mr, Mesh mesh, Matrix4x4 toRoot, List<Part> parts, List<string> locked)
        {
            if (!mesh.isReadable) { locked.Add(mr.name + " (merged level mesh)"); return; }
            var bounds = mr.bounds;
            bounds.Expand(0.02f);
            var mats = mr.sharedMaterials;
            var batchRoot = mr.staticBatchRootTransform;
            var toWorld = batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity;
            var p = new Part { Name = mr.name, ToRoot = toRoot * toWorld, Verts = mesh.vertices.ToArray(), Normals = mesh.normals?.ToArray(), Uvs = mesh.uv?.ToArray() };
            int matIndex = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var tri = mesh.GetTriangles(i).ToArray();
                if (tri.Length == 0) continue;
                bool inside = true;
                foreach (var k in tri)
                    if (!bounds.Contains(toWorld.MultiplyPoint3x4(p.Verts[k]))) { inside = false; break; }
                if (!inside) continue;
                p.Subs.Add(tri);
                p.Mats.Add(mats != null && mats.Count > 0 ? mats[Math.Min(matIndex++, mats.Count - 1)] : null);
            }
            if (p.Subs.Count == 0) { locked.Add(mr.name + " (could not find it in the merged level mesh)"); return; }
            parts.Add(p);
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
