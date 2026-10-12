using System.Collections.Generic;
using KerbalRelations.Core;
using UnityEngine;

namespace KerbalRelations.Visuals
{
    /// <summary>Builds the egg model and its textures at runtime; no model files are shipped.</summary>
    public static class EggMesh
    {
        private const int Rings = 18;
        private const int Segments = 28;
        private const int TextureWidth = 256;
        private const int TextureHeight = 128;
        private const int IconSize = 64;

        private static Mesh _mesh;
        private static Mesh _shape;
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        /// <summary>An egg standing upright, two units tall and centred on the origin, wide end down.</summary>
        public static Mesh Get()
        {
            return _mesh != null ? _mesh : (_mesh = Build(Rings, Segments, "KR_Egg"));
        }

        /// <summary>The same egg with few faces: what an egg that rolls around outside collides with.</summary>
        public static Mesh GetShape()
        {
            return _shape != null ? _shape : (_shape = Build(7, 10, "KR_EggShape"));
        }

        private static Mesh Build(int rings, int segments, string name)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= rings; ++ring)
            {
                var v = ring / (float)rings;
                // Evenly spaced along the outline rather than along the height, for round-looking ends.
                var y = -Mathf.Cos(v * Mathf.PI);
                var radius = Radius(y);
                var slope = OutlineNormal(y);
                for (int seg = 0; seg <= segments; ++seg)
                {
                    var u = seg / (float)segments;
                    var angle = u * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    vertices.Add(new Vector3(cos * radius, y, sin * radius));
                    normals.Add(new Vector3(cos * slope.x, slope.y, sin * slope.x).normalized);
                    uvs.Add(new Vector2(u, v));
                }
            }

            var stride = segments + 1;
            for (int ring = 0; ring < rings; ++ring)
            {
                for (int seg = 0; seg < segments; ++seg)
                {
                    var a = ring * stride + seg;
                    var b = a + stride;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Radius(float y)
        {
            return (float)EggPattern.EggHalfWidth(y) * 0.72f;
        }

        /// <summary>Outward normal of the outline at height y, as (radial, vertical).</summary>
        private static Vector2 OutlineNormal(float y)
        {
            if (y <= -0.999f)
                return new Vector2(0f, -1f);
            if (y >= 0.999f)
                return new Vector2(0f, 1f);
            const float d = 0.01f;
            var dr = Radius(Mathf.Min(0.9999f, y + d)) - Radius(Mathf.Max(-0.9999f, y - d));
            var dy = 2f * d;
            return new Vector2(dy, -dr).normalized;
        }

        /// <summary>A material with this egg's own colours and pattern.</summary>
        public static Material GetMaterial(string eggName)
        {
            Material material;
            if (Materials.TryGetValue(eggName, out material) && material != null)
                return material;

            // SetPixels32 fills the top mip level and Apply builds the rest. LoadRawTextureData would
            // want the data of every mip level.
            var bytes = EggPattern.Generate(eggName, TextureWidth, TextureHeight);
            var pixels = new Color32[TextureWidth * TextureHeight];
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            var texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, true);
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.Apply(true);

            var shader = Shader.Find("KSP/Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "KR_Egg_" + eggName, mainTexture = texture };
            Materials[eggName] = material;
            return material;
        }

        /// <summary>A flat picture of this egg for the relationship graph.</summary>
        public static Texture2D GetIcon(string eggName)
        {
            Texture2D icon;
            if (Icons.TryGetValue(eggName, out icon) && icon != null)
                return icon;

            icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            icon.LoadRawTextureData(EggPattern.GenerateIcon(eggName, IconSize));
            icon.wrapMode = TextureWrapMode.Clamp;
            icon.Apply(false);
            Icons[eggName] = icon;
            return icon;
        }
    }
}
