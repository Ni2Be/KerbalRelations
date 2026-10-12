using System.Collections.Generic;

namespace KerbalRelations.Core
{
    /// <summary>The persistent state of the simulation: every tracked Kerbal and every relationship.</summary>
    public class RelationsData
    {
        public const string NodeName = "KR_DATA";

        public readonly Dictionary<string, KerbalState> Kerbals = new Dictionary<string, KerbalState>();
        public readonly Dictionary<string, Relationship> Relationships = new Dictionary<string, Relationship>();
        /// <summary>UT of the last simulated moment, or -1 before the first tick.</summary>
        public double LastUT = -1d;
        /// <summary>Bodies a Kerbal has been born on or around, for the "first born" ribbons.</summary>
        public readonly HashSet<string> BirthBodies = new HashSet<string>();
        /// <summary>False until the ribbons for things that happened before ribbons existed are handed out.</summary>
        public bool RibbonsSeeded;

        public KerbalState Get(string name)
        {
            KerbalState state;
            return name != null && Kerbals.TryGetValue(name, out state) ? state : null;
        }

        public KerbalState GetOrCreate(string name, double baseHappiness)
        {
            KerbalState state;
            if (!Kerbals.TryGetValue(name, out state))
            {
                state = new KerbalState { Name = name, Happiness = baseHappiness, Social = baseHappiness };
                Kerbals.Add(name, state);
            }
            return state;
        }

        public Relationship GetRelationship(string a, string b)
        {
            Relationship rel;
            return a != null && b != null && Relationships.TryGetValue(PairKey.Make(a, b), out rel) ? rel : null;
        }

        public Relationship GetOrCreateRelationship(string a, string b, double now)
        {
            var key = PairKey.Make(a, b);
            Relationship rel;
            if (!Relationships.TryGetValue(key, out rel))
            {
                var first = string.CompareOrdinal(a, b) <= 0;
                rel = new Relationship
                {
                    A = first ? a : b,
                    B = first ? b : a,
                    KnownSince = now,
                    LastTogetherUT = now,
                    Family = IsKin(a, b)
                };
                Relationships.Add(key, rel);
            }
            return rel;
        }

        public List<Relationship> RelationshipsOf(string name)
        {
            var result = new List<Relationship>();
            foreach (var rel in Relationships.Values)
            {
                if (rel.Involves(name))
                    result.Add(rel);
            }
            return result;
        }

        /// <summary>Parent and child, or siblings sharing a parent.</summary>
        public bool IsKin(string a, string b)
        {
            var ka = Get(a);
            var kb = Get(b);
            if (ka == null || kb == null)
                return false;
            if (ka.HasParent(b) || kb.HasParent(a))
                return true;
            return (ka.ParentA != null && kb.HasParent(ka.ParentA)) || (ka.ParentB != null && kb.HasParent(ka.ParentB));
        }

        /// <summary>Removes a Kerbal and everything that points at them.</summary>
        public void Remove(string name)
        {
            if (!Kerbals.Remove(name))
                return;
            var dead = new List<string>();
            foreach (var pair in Relationships)
            {
                if (pair.Value.Involves(name))
                    dead.Add(pair.Key);
            }
            for (int i = 0; i < dead.Count; ++i)
                Relationships.Remove(dead[i]);
            foreach (var other in Kerbals.Values)
            {
                if (other.Partner == name)
                {
                    other.Partner = null;
                    other.PartnerApartSince = -1d;
                }
            }
        }

        public void Rename(string oldName, string newName)
        {
            KerbalState state;
            if (oldName == newName || !Kerbals.TryGetValue(oldName, out state) || Kerbals.ContainsKey(newName))
                return;
            Kerbals.Remove(oldName);
            state.Name = newName;
            Kerbals.Add(newName, state);

            foreach (var other in Kerbals.Values)
            {
                if (other.Partner == oldName)
                    other.Partner = newName;
                if (other.ParentA == oldName)
                    other.ParentA = newName;
                if (other.ParentB == oldName)
                    other.ParentB = newName;
                if (other.BestCompany == oldName)
                    other.BestCompany = newName;
            }

            var moved = new List<Relationship>();
            var dead = new List<string>();
            foreach (var pair in Relationships)
            {
                if (pair.Value.Involves(oldName))
                {
                    dead.Add(pair.Key);
                    moved.Add(pair.Value);
                }
            }
            for (int i = 0; i < dead.Count; ++i)
                Relationships.Remove(dead[i]);
            for (int i = 0; i < moved.Count; ++i)
            {
                var rel = moved[i];
                var other = rel.Other(oldName);
                var first = string.CompareOrdinal(newName, other) <= 0;
                rel.A = first ? newName : other;
                rel.B = first ? other : newName;
                Relationships[PairKey.Make(newName, other)] = rel;
            }
        }

        public DataNode Save()
        {
            var root = new DataNode(NodeName);
            root.Add("lastUT", LastUT);
            root.Add("ribbonsSeeded", RibbonsSeeded);
            foreach (var body in BirthBodies)
                root.Add("birthBody", body);

            foreach (var k in Kerbals.Values)
            {
                var node = root.AddNode("KERBAL");
                node.Add("name", k.Name);
                node.Add("sex", k.Sex.ToString());
                node.Add("stage", k.Stage.ToString());
                node.Add("happiness", k.Happiness);
                node.Add("social", k.Social);
                node.Add("aloneSince", k.AloneSince);
                node.Add("partnerApartSince", k.PartnerApartSince);
                node.Add("stageStartUT", k.StageStartUT);
                node.Add("birthUT", k.BirthUT);
                if (k.BirthPlace != null)
                    node.Add("birthPlace", k.BirthPlace);
                if (k.BirthBody != null)
                    node.Add("birthBody", k.BirthBody);
                node.Add("awaySince", k.AwaySince);
                if (k.Visitor)
                    node.Add("visitor", true);
                node.Add("lastBaseXp", k.LastBaseXp);
                if (k.ParentA != null)
                    node.Add("parentA", k.ParentA);
                if (k.ParentB != null)
                    node.Add("parentB", k.ParentB);
                if (k.Partner != null)
                    node.Add("partner", k.Partner);
                foreach (var body in k.VisitedBodies)
                    node.Add("visited", body);
                foreach (var body in k.LandedBodies)
                    node.Add("landed", body);
                for (int i = 0; i < k.Modifiers.Count; ++i)
                {
                    var m = k.Modifiers[i];
                    var mNode = node.AddNode("MODIFIER");
                    mNode.Add("id", m.Id);
                    mNode.Add("text", m.Text);
                    if (m.Subject != null)
                        mNode.Add("subject", m.Subject);
                    mNode.Add("amount", m.Amount);
                    mNode.Add("startUT", m.StartUT);
                    mNode.Add("expiresUT", m.ExpiresUT);
                    mNode.Add("noFade", m.NoFade);
                }
                for (int i = 0; i < k.Log.Count; ++i)
                {
                    var l = k.Log[i];
                    var lNode = node.AddNode("LOG");
                    lNode.Add("ut", l.UT);
                    lNode.Add("text", l.Text);
                    lNode.Add("amount", l.Amount);
                }
                for (int i = 0; i < k.Ribbons.Count; ++i)
                {
                    var rNode = node.AddNode("RIBBON");
                    rNode.Add("id", k.Ribbons[i].Id);
                    rNode.Add("ut", k.Ribbons[i].UT);
                }
            }

            foreach (var r in Relationships.Values)
            {
                var node = root.AddNode("RELATIONSHIP");
                node.Add("a", r.A);
                node.Add("b", r.B);
                node.Add("strength", r.Strength);
                node.Add("knownSince", r.KnownSince);
                node.Add("lastTogetherUT", r.LastTogetherUT);
                node.Add("isCouple", r.IsCouple);
                node.Add("coupleSince", r.CoupleSince);
                node.Add("lastEggUT", r.LastEggUT);
                node.Add("eggPendingSince", r.EggPendingSince);
                node.Add("family", r.Family);
            }
            return root;
        }

        public void Load(DataNode root)
        {
            Kerbals.Clear();
            Relationships.Clear();
            BirthBodies.Clear();
            RibbonsSeeded = false;
            LastUT = -1d;
            if (root == null)
                return;

            LastUT = root.GetDouble("lastUT", -1d);
            RibbonsSeeded = root.GetBool("ribbonsSeeded", false);
            foreach (var body in root.GetAll("birthBody"))
                BirthBodies.Add(body);

            foreach (var node in root.GetNodes("KERBAL"))
            {
                var name = node.Get("name");
                if (string.IsNullOrEmpty(name) || Kerbals.ContainsKey(name))
                    continue;
                var k = new KerbalState { Name = name };
                k.Sex = node.Get("sex") == Sex.Female.ToString() ? Sex.Female : Sex.Male;
                var stage = node.Get("stage");
                k.Stage = stage == LifeStage.Egg.ToString() ? LifeStage.Egg
                    : stage == LifeStage.Child.ToString() ? LifeStage.Child
                    : LifeStage.Adult;
                k.Happiness = node.GetDouble("happiness", 70d);
                k.Social = node.GetDouble("social", k.Happiness);
                k.AloneSince = node.GetDouble("aloneSince", -1d);
                k.PartnerApartSince = node.GetDouble("partnerApartSince", -1d);
                k.StageStartUT = node.GetDouble("stageStartUT", 0d);
                k.BirthUT = node.GetDouble("birthUT", -1d);
                k.BirthPlace = node.Get("birthPlace");
                k.BirthBody = node.Get("birthBody");
                k.AwaySince = node.GetDouble("awaySince", -1d);
                k.Visitor = node.GetBool("visitor", false);
                k.LastBaseXp = node.GetDouble("lastBaseXp", -1d);
                k.ParentA = node.Get("parentA");
                k.ParentB = node.Get("parentB");
                k.Partner = node.Get("partner");
                foreach (var body in node.GetAll("visited"))
                    k.VisitedBodies.Add(body);
                foreach (var body in node.GetAll("landed"))
                    k.LandedBodies.Add(body);
                foreach (var mNode in node.GetNodes("MODIFIER"))
                {
                    k.Modifiers.Add(new ActiveModifier
                    {
                        Id = mNode.Get("id", string.Empty),
                        Text = mNode.Get("text", string.Empty),
                        Subject = mNode.Get("subject"),
                        Amount = mNode.GetDouble("amount", 0d),
                        StartUT = mNode.GetDouble("startUT", 0d),
                        ExpiresUT = mNode.GetDouble("expiresUT", 0d),
                        NoFade = mNode.GetBool("noFade", false)
                    });
                }
                foreach (var lNode in node.GetNodes("LOG"))
                    k.AddLog(lNode.GetDouble("ut", 0d), lNode.Get("text", string.Empty), lNode.GetDouble("amount", 0d));
                foreach (var rNode in node.GetNodes("RIBBON"))
                {
                    var id = rNode.Get("id");
                    if (!string.IsNullOrEmpty(id) && !k.HasRibbon(id))
                        k.Ribbons.Add(new RibbonAward { Id = id, UT = rNode.GetDouble("ut", 0d) });
                }
                Kerbals.Add(name, k);
            }

            foreach (var node in root.GetNodes("RELATIONSHIP"))
            {
                var a = node.Get("a");
                var b = node.Get("b");
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
                    continue;
                if (!Kerbals.ContainsKey(a) || !Kerbals.ContainsKey(b))
                    continue;
                var first = string.CompareOrdinal(a, b) <= 0;
                Relationships[PairKey.Make(a, b)] = new Relationship
                {
                    A = first ? a : b,
                    B = first ? b : a,
                    Strength = node.GetDouble("strength", 0d),
                    KnownSince = node.GetDouble("knownSince", 0d),
                    LastTogetherUT = node.GetDouble("lastTogetherUT", 0d),
                    IsCouple = node.GetBool("isCouple", false),
                    CoupleSince = node.GetDouble("coupleSince", -1d),
                    LastEggUT = node.GetDouble("lastEggUT", -1d),
                    EggPendingSince = node.GetDouble("eggPendingSince", -1d),
                    Family = node.GetBool("family", false)
                };
            }

            // A partner link is only valid if both sides and the couple record agree.
            foreach (var k in Kerbals.Values)
            {
                if (k.Partner == null)
                    continue;
                var rel = GetRelationship(k.Name, k.Partner);
                var other = Get(k.Partner);
                if (rel == null || !rel.IsCouple || other == null || other.Partner != k.Name)
                    k.Partner = null;
            }
            foreach (var rel in Relationships.Values)
            {
                if (rel.IsCouple && (Kerbals[rel.A].Partner != rel.B || Kerbals[rel.B].Partner != rel.A))
                    rel.IsCouple = false;
            }
        }
    }
}
