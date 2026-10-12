using System;
using System.Collections.Generic;

namespace KerbalRelations.Core
{
    public class GraphNode
    {
        public string Id;
        public double X;
        public double Y;
        public double VX;
        public double VY;
        /// <summary>Held in place (being dragged).</summary>
        public bool Pinned;
    }

    public struct GraphEdge
    {
        public string A;
        public string B;
        /// <summary>0-100; stronger edges pull their nodes closer.</summary>
        public double Strength;
    }

    /// <summary>
    /// Force-directed layout around the origin: all nodes repel each other, every edge is a spring whose
    /// rest length shrinks with strength, and a weak pull towards the centre keeps strangers on screen.
    /// </summary>
    public class GraphLayout
    {
        public readonly Dictionary<string, GraphNode> Nodes = new Dictionary<string, GraphNode>();

        public double Width = 620d;
        public double Height = 420d;
        public double Margin = 36d;
        public double Repulsion = 14000d;
        public double SpringStiffness = 0.05d;
        public double MinLength = 70d;
        public double MaxLength = 230d;
        public double Gravity = 0.012d;
        public double Damping = 0.8d;
        /// <summary>How much of the force is applied per step; lower is steadier but slower.</summary>
        public double StepSize = 0.2d;
        /// <summary>Softens the repulsion of nodes that are almost touching.</summary>
        public double Softening = 600d;
        public double MaxSpeed = 14d;

        /// <summary>Adds missing nodes (at a position derived from the id) and drops the ones no longer wanted.</summary>
        public void Sync(ICollection<string> ids)
        {
            var stale = new List<string>();
            foreach (var id in Nodes.Keys)
            {
                if (!ids.Contains(id))
                    stale.Add(id);
            }
            for (int i = 0; i < stale.Count; ++i)
                Nodes.Remove(stale[i]);

            foreach (var id in ids)
            {
                if (Nodes.ContainsKey(id))
                    continue;
                var angle = StableHash.Unit(id) * Math.PI * 2d;
                var radius = Math.Min(Width, Height) * (0.2d + 0.2d * StableHash.Unit(id + "#"));
                Nodes.Add(id, new GraphNode { Id = id, X = Math.Cos(angle) * radius, Y = Math.Sin(angle) * radius });
            }
        }

        public double RestLength(double strength)
        {
            var s = Math.Max(0d, Math.Min(100d, strength)) / 100d;
            return MaxLength + (MinLength - MaxLength) * s;
        }

        /// <summary>One relaxation step. Returns the total movement, which approaches zero as the layout settles.</summary>
        public double Step(IList<GraphEdge> edges)
        {
            var list = new List<GraphNode>(Nodes.Values);
            var fx = new double[list.Count];
            var fy = new double[list.Count];
            var index = new Dictionary<string, int>(list.Count);
            for (int i = 0; i < list.Count; ++i)
                index[list[i].Id] = i;

            for (int i = 0; i < list.Count; ++i)
            {
                for (int j = i + 1; j < list.Count; ++j)
                {
                    var dx = list[i].X - list[j].X;
                    var dy = list[i].Y - list[j].Y;
                    var d2 = dx * dx + dy * dy;
                    if (d2 < 1d)
                    {
                        // Coincident nodes: push apart in a direction fixed by their ids.
                        var angle = StableHash.Unit(list[i].Id + list[j].Id) * Math.PI * 2d;
                        dx = Math.Cos(angle);
                        dy = Math.Sin(angle);
                        d2 = 1d;
                    }
                    var d = Math.Sqrt(d2);
                    var force = Repulsion / (d2 + Softening);
                    fx[i] += dx / d * force;
                    fy[i] += dy / d * force;
                    fx[j] -= dx / d * force;
                    fy[j] -= dy / d * force;
                }
                fx[i] -= list[i].X * Gravity;
                fy[i] -= list[i].Y * Gravity;
            }

            for (int e = 0; e < edges.Count; ++e)
            {
                int i, j;
                if (!index.TryGetValue(edges[e].A, out i) || !index.TryGetValue(edges[e].B, out j) || i == j)
                    continue;
                var dx = list[j].X - list[i].X;
                var dy = list[j].Y - list[i].Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 0.001d)
                    continue;
                var force = (d - RestLength(edges[e].Strength)) * SpringStiffness;
                fx[i] += dx / d * force;
                fy[i] += dy / d * force;
                fx[j] -= dx / d * force;
                fy[j] -= dy / d * force;
            }

            var moved = 0d;
            var halfW = Math.Max(1d, Width / 2d - Margin);
            var halfH = Math.Max(1d, Height / 2d - Margin);
            for (int i = 0; i < list.Count; ++i)
            {
                var node = list[i];
                if (node.Pinned)
                {
                    node.VX = 0d;
                    node.VY = 0d;
                    continue;
                }
                node.VX = node.VX * Damping + fx[i] * StepSize;
                node.VY = node.VY * Damping + fy[i] * StepSize;
                var speed = Math.Sqrt(node.VX * node.VX + node.VY * node.VY);
                if (speed > MaxSpeed)
                {
                    node.VX *= MaxSpeed / speed;
                    node.VY *= MaxSpeed / speed;
                }
                // A node pressed against the border stops there instead of pushing on forever.
                var x = node.X + node.VX;
                var y = node.Y + node.VY;
                if (x < -halfW || x > halfW)
                {
                    x = Math.Max(-halfW, Math.Min(halfW, x));
                    node.VX = 0d;
                }
                if (y < -halfH || y > halfH)
                {
                    y = Math.Max(-halfH, Math.Min(halfH, y));
                    node.VY = 0d;
                }
                moved += Math.Abs(x - node.X) + Math.Abs(y - node.Y);
                node.X = x;
                node.Y = y;
            }
            return moved;
        }

        public void Run(IList<GraphEdge> edges, int iterations)
        {
            for (int i = 0; i < iterations; ++i)
                Step(edges);
        }

        public double Distance(string a, string b)
        {
            var na = Nodes[a];
            var nb = Nodes[b];
            var dx = na.X - nb.X;
            var dy = na.Y - nb.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
