using System.Collections.Generic;
using System.Text;
using KerbalRelations.Core;
using UnityEngine;

namespace KerbalRelations.Visuals
{
    /// <summary>
    /// Lives on the model of one crew member and makes it look like an egg or a child: on the Kerbal
    /// sitting in a cabin, and on a child walking around outside. Removing the component puts the stock
    /// Kerbal back. (An egg outside is an <see cref="EggBall"/>.)
    /// </summary>
    public class StageVisual : MonoBehaviour
    {
        /// <summary>A texture counts as a head when at least this much of it is skin; the stock head is at 45 %.</summary>
        private const float MinSkinShare = 0.12f;

        private LifeStage _stage = LifeStage.Adult;
        private ProtoCrewMember _crew;
        /// <summary>Everything of this Kerbal hangs under it.</summary>
        private Transform _root;
        /// <summary>The group that holds the head meshes, where the model has one.</summary>
        private Transform _head;
        /// <summary>A transform that has "eye" in its name but is not an eye.</summary>
        private Transform _notABone;
        /// <summary>The Kerbal outside a vessel, or null in a cabin.</summary>
        private KerbalEVA _eva;

        // Egg
        private Renderer[] _hidden;
        private bool[] _wasEnabled;
        private GameObject _egg;
        private Quaternion _eggRotation;
        private float _phase;

        // Child
        private Transform _body;
        private Vector3 _bodyScale;
        private Vector3 _bodyPosition;
        private Vector3 _bodyWritten;
        private Vector3 _feet;
        private float _stumbleThreshold;
        private bool _noSkeleton;
        private readonly List<Transform> _headBones = new List<Transform>();
        private readonly List<Vector3> _headScales = new List<Vector3>();
        private readonly List<SkinnedMeshRenderer> _eyeMeshes = new List<SkinnedMeshRenderer>();
        private readonly List<Transform[]> _eyeMeshBones = new List<Transform[]>();
        private readonly List<Transform> _eyeProxies = new List<Transform>();
        private readonly List<Vector3> _eyePivots = new List<Vector3>();
        private readonly List<Renderer> _skin = new List<Renderer>();
        private readonly List<Texture> _skinOriginals = new List<Texture>();
        private readonly List<Texture2D> _skinTinted = new List<Texture2D>();
        private bool _skinOn;
        private float _skinHue;
        private float _skinSaturation;

        /// <summary>For the Kerbal sitting in a cabin.</summary>
        public void Apply(Kerbal kerbal, LifeStage stage, string kerbalName)
        {
            if (stage == _stage)
                return;
            Restore();
            _eva = null;
            _crew = kerbal.protoCrewMember;
            _root = kerbal.transform;
            _head = kerbal.headTransform;
            _notABone = kerbal.eyeTransform;
            _stage = stage;
            if (stage == LifeStage.Egg)
            {
                ApplyEgg(kerbalName);
            }
            else if (stage == LifeStage.Child)
            {
                // The whole seated model shrinks about its own origin, the seat.
                _body = _root;
                ApplyChild(kerbalName);
            }
        }

        /// <summary>For a child outside a vessel.</summary>
        public void ApplyEva(KerbalEVA eva, ProtoCrewMember crew)
        {
            if (_eva == eva && (_stage == LifeStage.Child || _noSkeleton))
                return;
            Restore();
            _eva = eva;
            _crew = crew;
            _root = eva.transform;
            _head = null;
            _notABone = null;

            // Out here the Kerbal is a physical thing. Shrinking all of it would shrink what it walks
            // and climbs with as well, so only the skeleton shrinks, towards the feet.
            _body = SkeletonTop(_root);
            _noSkeleton = _body == null;
            if (_noSkeleton)
            {
                Log.Warn("No skeleton found in the model of " + crew.name + "; the child keeps its adult size outside");
                return;
            }
            _stage = LifeStage.Child;
            _feet = new Vector3(0f, LowestPoint(eva), 0f);

            // A shrunk skeleton does not survive going limp, so children do not stumble.
            _stumbleThreshold = eva.stumbleThreshold;
            eva.stumbleThreshold = 1e6f;

            Log.Info(string.Format("Child model of {0} outside: skeleton {1}, feet at {2:0.00} (foot pivot {3})", crew.name, _body.name, _feet.y,
                eva.footPivot != null ? _root.InverseTransformPoint(eva.footPivot.position).y.ToString("0.00") : "none"));
            ApplyChild(crew.name);
        }

        /// <summary>The child of the root that carries the bones of the skinned meshes.</summary>
        private static Transform SkeletonTop(Transform root)
        {
            var counts = new Dictionary<Transform, int>();
            Transform best = null;
            foreach (var mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = mesh.bones;
                for (int i = 0; i < bones.Length; ++i)
                {
                    var top = bones[i];
                    while (top != null && top.parent != root)
                        top = top.parent;
                    if (top == null)
                        continue;
                    int count;
                    counts.TryGetValue(top, out count);
                    counts[top] = count + 1;
                    if (best == null || counts[top] > counts[best])
                        best = top;
                }
            }
            return best;
        }

        /// <summary>How far below its origin the Kerbal touches the ground, from what it collides with.</summary>
        private float LowestPoint(KerbalEVA eva)
        {
            var lowest = 0f;
            var body = eva.GetComponent<Rigidbody>();
            foreach (var collider in _root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                    continue;
                // The limbs carry colliders of their own for when the Kerbal goes limp.
                if (body != null && collider.attachedRigidbody != body)
                    continue;
                var scale = collider.transform.lossyScale.y / Mathf.Max(0.001f, _root.lossyScale.y);
                float centre, reach;
                var capsule = collider as CapsuleCollider;
                var sphere = collider as SphereCollider;
                var box = collider as BoxCollider;
                if (capsule != null)
                {
                    centre = _root.InverseTransformPoint(capsule.transform.TransformPoint(capsule.center)).y;
                    reach = Mathf.Max(capsule.radius, capsule.direction == 1 ? capsule.height * 0.5f : capsule.radius) * scale;
                }
                else if (sphere != null)
                {
                    centre = _root.InverseTransformPoint(sphere.transform.TransformPoint(sphere.center)).y;
                    reach = sphere.radius * scale;
                }
                else if (box != null)
                {
                    centre = _root.InverseTransformPoint(box.transform.TransformPoint(box.center)).y;
                    reach = box.size.y * 0.5f * scale;
                }
                else
                {
                    continue;
                }
                lowest = Mathf.Min(lowest, centre - reach);
            }
            if (lowest == 0f && eva.footPivot != null)
                lowest = Mathf.Min(0f, _root.InverseTransformPoint(eva.footPivot.position).y);
            return lowest;
        }

        private void ApplyEgg(string kerbalName)
        {
            _hidden = _root.GetComponentsInChildren<Renderer>(true);
            _wasEnabled = new bool[_hidden.Length];

            // Put the egg where the Kerbal's body was and render it on the same layer, so both the cabin
            // view and the crew portrait camera show it.
            var layer = _root.gameObject.layer;
            var bounds = new Bounds();
            var hasBounds = false;
            for (int i = 0; i < _hidden.Length; ++i)
            {
                var r = _hidden[i];
                _wasEnabled[i] = r.enabled;
                if (r.enabled && r.gameObject.activeInHierarchy && r is SkinnedMeshRenderer)
                {
                    if (hasBounds)
                        bounds.Encapsulate(r.bounds);
                    else
                        bounds = r.bounds;
                    hasBounds = true;
                    layer = r.gameObject.layer;
                }
                r.enabled = false;
            }

            var height = ConfigLoader.EggSize;
            var position = _root.position + _root.up * 0.3f;
            // A sane bounding box is roughly Kerbal sized; skinned bounds can be far off, so check.
            if (hasBounds && bounds.size.y > 0.2f && bounds.size.y < 2f)
            {
                position = bounds.center;
                height = Mathf.Clamp(bounds.size.y * ConfigLoader.EggSize, 0.25f, 0.9f);
            }

            _egg = new GameObject("KR_Egg") { layer = layer };
            _egg.transform.SetParent(_root, false);
            _egg.transform.position = position;
            _egg.transform.rotation = _root.rotation;
            var parentScale = _root.lossyScale;
            // The mesh is two units tall.
            _egg.transform.localScale = new Vector3(
                height / 2f / Mathf.Max(0.001f, parentScale.x),
                height / 2f / Mathf.Max(0.001f, parentScale.y),
                height / 2f / Mathf.Max(0.001f, parentScale.z));
            _eggRotation = _egg.transform.localRotation;
            _phase = (float)(StableHash.Unit(kerbalName) * 10d);

            _egg.AddComponent<MeshFilter>().sharedMesh = EggMesh.Get();
            _egg.AddComponent<MeshRenderer>().sharedMaterial = EggMesh.GetMaterial(kerbalName);
        }

        private void ApplyChild(string kerbalName)
        {
            _bodyScale = _body.localScale;
            _bodyPosition = _body.localPosition;
            _bodyWritten = _bodyPosition;

            // The meshes are skinned: they follow the skeleton and ignore the transforms they hang on,
            // so the proportions are changed on the bones. The neck carries the whole head; the helmet
            // hangs next to it and has to grow along.
            var all = _root.GetComponentsInChildren<Transform>(true);
            AddBones(all, "neck", _headBones, _headScales);
            if (_headBones.Count == 0)
                AddBones(all, "head", _headBones, _headScales);
            AddBones(all, "helmet", _headBones, _headScales);

            var eyeJoints = new List<Transform>();
            AddBones(all, "eye", eyeJoints, new List<Vector3>());
            var eyes = new StringBuilder();
            foreach (var mesh in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var name = mesh.name.ToLowerInvariant();
                if (!name.Contains("eyeball") && !name.Contains("pupil"))
                    continue;
                var joint = EyeJoint(name, mesh, eyeJoints);
                AddEyeMesh(mesh, joint != null ? joint.position : mesh.bounds.center);
                eyes.Append(eyes.Length > 0 ? ", " : string.Empty).Append(mesh.name).Append(" on ").Append(joint != null ? joint.name : "its own centre");
            }

            EnforceChildScale();
            var skin = TintSkin();
            Log.Info(string.Format("Child model of {0}: head bones [{1}], eyes [{2}], skin [{3}]", kerbalName, Names(_headBones), eyes, skin));
        }

        /// <summary>The eye joint of the side this mesh is on; it sits in the middle of the eyeball.</summary>
        private static Transform EyeJoint(string meshName, SkinnedMeshRenderer mesh, List<Transform> joints)
        {
            var side = meshName.Contains("left") ? "_l_" : meshName.Contains("right") ? "_r_" : null;
            Transform nearest = null;
            var nearestDistance = float.MaxValue;
            var centre = mesh.bounds.center;
            for (int i = 0; i < joints.Count; ++i)
            {
                if (side != null && joints[i].name.ToLowerInvariant().Contains(side))
                    return joints[i];
                var distance = (joints[i].position - centre).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = joints[i];
                }
            }
            return nearest;
        }

        /// <summary>
        /// The white of the eye is skinned to the skull and only the pupil to the eye joint, so scaling
        /// a bone cannot enlarge the whole eye. Instead every bone of an eye mesh gets a stand-in that
        /// scales about the middle of the eye, and the mesh is skinned to the stand-ins.
        /// </summary>
        private void AddEyeMesh(SkinnedMeshRenderer mesh, Vector3 pivot)
        {
            var bones = mesh.bones;
            var proxies = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; ++i)
            {
                if (bones[i] == null)
                    continue;
                var proxy = new GameObject("KR_Proxy") { layer = bones[i].gameObject.layer }.transform;
                proxy.SetParent(bones[i], false);
                proxies[i] = proxy;
                _eyeProxies.Add(proxy);
                _eyePivots.Add(bones[i].InverseTransformPoint(pivot));
            }
            _eyeMeshes.Add(mesh);
            _eyeMeshBones.Add(bones);
            mesh.bones = proxies;
        }

        /// <summary>
        /// Gives the head a recoloured copy of its texture. In a cabin the skin is on the meshes under
        /// the head transform (the Kerbal's textureTargets are the suit, which stock swaps for
        /// veterans). The model outside has no such group; there the textures that are largely skin
        /// coloured are the ones.
        /// </summary>
        /// <returns>What was recoloured, for the log.</returns>
        private string TintSkin()
        {
            _skinOn = WantedSkin(out _skinHue, out _skinSaturation);
            if (!_skinOn)
                return "off";
            var head = _head != null ? _head : _root;
            var targets = head.GetComponentsInChildren<Renderer>(true);
            var report = new StringBuilder();
            for (int i = 0; i < targets.Length; ++i)
            {
                var target = targets[i];
                var material = target.sharedMaterial;
                if (material == null || !material.HasProperty("_MainTex"))
                    continue;
                var original = material.mainTexture;
                // The flag on the suit can be any colour, skin colour included.
                if (_head == null && (target.name.ToLowerInvariant().Contains("flag") || SkinTexture.SkinShare(original) < MinSkinShare))
                    continue;
                var tinted = SkinTexture.Get(original, _skinHue, _skinSaturation);
                if (tinted == null)
                    continue;
                target.material.mainTexture = tinted;
                _skin.Add(target);
                _skinOriginals.Add(original);
                _skinTinted.Add(tinted);
                report.Append(report.Length > 0 ? ", " : string.Empty).Append(target.name).Append(" <- ").Append(original.name);
            }
            return report.Length > 0 ? "hue " + _skinHue.ToString("0") + ": " + report : "nothing found under " + head.name;
        }

        /// <summary>The skin colour of this child; girls can have their own. False if the skin stays green.</summary>
        private bool WantedSkin(out float hue, out float saturation)
        {
            var girl = _crew != null && _crew.gender == ProtoCrewMember.Gender.Female && ConfigLoader.ChildSkinHueFemale >= 0f;
            hue = girl ? ConfigLoader.ChildSkinHueFemale : ConfigLoader.ChildSkinHue;
            saturation = girl ? ConfigLoader.ChildSkinSaturationFemale : 1f;
            return hue >= 0f;
        }

        /// <summary>Stock or another mod may set the texture again later; keep the recoloured one on.</summary>
        private void EnforceSkin()
        {
            // The debug sliders change the colour while playing, and throw the old copies away.
            float hue, saturation;
            var on = WantedSkin(out hue, out saturation);
            if (on != _skinOn || hue != _skinHue || saturation != _skinSaturation || (_skinTinted.Count > 0 && _skinTinted[0] == null))
            {
                RestoreSkin();
                TintSkin();
                return;
            }

            for (int i = 0; i < _skin.Count; ++i)
            {
                var material = _skin[i] != null ? _skin[i].sharedMaterial : null;
                if (material == null || !material.HasProperty("_MainTex"))
                    continue;
                var current = material.mainTexture;
                if (current == _skinTinted[i])
                    continue;
                var tinted = SkinTexture.Get(current, _skinHue, _skinSaturation);
                if (tinted == null)
                    continue;
                _skinOriginals[i] = current;
                _skinTinted[i] = tinted;
                material.mainTexture = tinted;
            }
        }

        private void RestoreSkin()
        {
            for (int i = 0; i < _skin.Count; ++i)
            {
                if (_skin[i] != null && _skin[i].sharedMaterial != null && _skin[i].sharedMaterial.mainTexture == _skinTinted[i])
                    _skin[i].sharedMaterial.mainTexture = _skinOriginals[i];
            }
            _skin.Clear();
            _skinOriginals.Clear();
            _skinTinted.Clear();
        }

        /// <summary>
        /// Adds the bones with this word in their name. Only the top-most of a chain is taken, because
        /// scaling a bone already scales what hangs below it.
        /// </summary>
        private void AddBones(Transform[] all, string word, List<Transform> bones, List<Vector3> scales)
        {
            var first = bones.Count;
            for (int i = 0; i < all.Length; ++i)
            {
                var t = all[i];
                if (t == _root || t == _body || t == _notABone || !t.name.ToLowerInvariant().Contains(word))
                    continue;
                // Bones carry no renderers; the mesh objects and the portrait camera are not bones.
                // Outside, lamps and tools hang on the bones, so only the transform itself is looked at.
                if (t.GetComponent<Camera>() != null
                    || (_eva != null ? t.GetComponent<Renderer>() != null : t.GetComponentInChildren<Renderer>(true) != null))
                    continue;
                var nested = false;
                for (var p = t.parent; p != null && !nested; p = p.parent)
                    nested = bones.IndexOf(p, first) >= 0;
                if (nested)
                    continue;
                bones.Add(t);
                scales.Add(t.localScale);
            }
        }

        private static string Names(List<Transform> bones)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bones.Count; ++i)
                sb.Append(i > 0 ? ", " : string.Empty).Append(bones[i].name);
            return sb.ToString();
        }

        private void EnforceChildScale()
        {
            var scale = ConfigLoader.ChildScale;
            _body.localScale = _bodyScale * scale;
            if (_eva != null)
            {
                // Animations may move the skeleton: what they wrote this frame is the pose to shrink.
                // If nothing wrote, the value is still the one from here, and the last pose stands.
                if (_body.localPosition != _bodyWritten)
                    _bodyPosition = _body.localPosition;
                _bodyWritten = _bodyPosition * scale + _feet * (1f - scale);
                _body.localPosition = _bodyWritten;
            }
            for (int i = 0; i < _headBones.Count; ++i)
            {
                if (_headBones[i] != null)
                    _headBones[i].localScale = _headScales[i] * ConfigLoader.ChildHeadScale;
            }
            var eyeScale = ConfigLoader.ChildEyeScale;
            for (int i = 0; i < _eyeProxies.Count; ++i)
            {
                if (_eyeProxies[i] == null)
                    continue;
                _eyeProxies[i].localScale = new Vector3(eyeScale, eyeScale, eyeScale);
                _eyeProxies[i].localPosition = _eyePivots[i] * (1f - eyeScale);
            }
        }

        public void LateUpdate()
        {
            if (_stage == LifeStage.Egg)
            {
                // Stock switches head renderers back on when leaving the first-person view.
                if (_hidden != null)
                {
                    for (int i = 0; i < _hidden.Length; ++i)
                    {
                        if (_hidden[i] != null && _hidden[i].enabled)
                            _hidden[i].enabled = false;
                    }
                }
                if (_egg != null)
                {
                    var t = Time.time * 1.7f + _phase;
                    var wobble = Quaternion.Euler(Mathf.Sin(t) * 4f, 0f, Mathf.Sin(t * 0.63f + 1f) * 4f);
                    _egg.transform.localRotation = _eggRotation * wobble;
                }
            }
            else if (_stage == LifeStage.Child && _body != null)
            {
                // Animations may write scales; keep the child proportions every frame.
                EnforceChildScale();
                EnforceSkin();
            }
        }

        private void Restore()
        {
            if (_stage == LifeStage.Egg)
            {
                if (_egg != null)
                    Destroy(_egg);
                _egg = null;
                if (_hidden != null)
                {
                    for (int i = 0; i < _hidden.Length; ++i)
                    {
                        if (_hidden[i] != null)
                            _hidden[i].enabled = _wasEnabled[i];
                    }
                }
                _hidden = null;
            }
            else if (_stage == LifeStage.Child)
            {
                if (_body != null)
                {
                    _body.localScale = _bodyScale;
                    if (_eva != null)
                        _body.localPosition = _bodyPosition;
                }
                if (_eva != null)
                    _eva.stumbleThreshold = _stumbleThreshold;
                for (int i = 0; i < _headBones.Count; ++i)
                {
                    if (_headBones[i] != null)
                        _headBones[i].localScale = _headScales[i];
                }
                for (int i = 0; i < _eyeMeshes.Count; ++i)
                {
                    if (_eyeMeshes[i] != null)
                        _eyeMeshes[i].bones = _eyeMeshBones[i];
                }
                for (int i = 0; i < _eyeProxies.Count; ++i)
                {
                    if (_eyeProxies[i] != null)
                        Destroy(_eyeProxies[i].gameObject);
                }
                RestoreSkin();
                _headBones.Clear();
                _headScales.Clear();
                _eyeMeshes.Clear();
                _eyeMeshBones.Clear();
                _eyeProxies.Clear();
                _eyePivots.Clear();
            }
            _body = null;
            _stage = LifeStage.Adult;
        }

        public void OnDestroy()
        {
            Restore();
        }

        /// <summary>
        /// Writes a model's transform tree to the log, with what it is drawn with and collides with;
        /// used to tune which bones and textures are picked.
        /// </summary>
        public static void DumpHierarchy(Transform root, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            Dump(root, 0, sb);
            Log.Info(sb.ToString());
        }

        public static void DumpHierarchy(Kerbal kerbal)
        {
            DumpHierarchy(kerbal.transform, "Cabin model of " + (kerbal.protoCrewMember != null ? kerbal.protoCrewMember.name : kerbal.name)
                + ": headTransform = " + (kerbal.headTransform != null ? kerbal.headTransform.name : "null")
                + ", eyeTransform = " + (kerbal.eyeTransform != null ? kerbal.eyeTransform.name : "null"));
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.name);
            sb.Append("  pos=").Append(t.localPosition.ToString("F2"));
            sb.Append(" scale=").Append(t.localScale.ToString("F2"));
            sb.Append(" layer=").Append(t.gameObject.layer);
            if (!t.gameObject.activeSelf)
                sb.Append(" inactive");
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = renderer.sharedMaterial;
                var texture = material != null && material.HasProperty("_MainTex") ? material.mainTexture : null;
                sb.Append(" [").Append(renderer.GetType().Name).Append(renderer.enabled ? "" : " off")
                    .Append(texture != null ? " tex=" + texture.name : string.Empty).Append("]");
            }
            foreach (var collider in t.GetComponents<Collider>())
            {
                sb.Append(" [").Append(collider.GetType().Name).Append(collider.enabled ? "" : " off").Append(collider.isTrigger ? " trigger" : "");
                var capsule = collider as CapsuleCollider;
                if (capsule != null)
                    sb.Append(" c=").Append(capsule.center.ToString("F2")).Append(" r=").Append(capsule.radius.ToString("F2"))
                        .Append(" h=").Append(capsule.height.ToString("F2")).Append(" dir=").Append(capsule.direction);
                sb.Append("]");
            }
            if (t.GetComponent<Rigidbody>() != null)
                sb.Append(" [Rigidbody]");
            if (t.GetComponent<Camera>() != null)
                sb.Append(" [Camera]");
            sb.AppendLine();
            for (int i = 0; i < t.childCount; ++i)
                Dump(t.GetChild(i), depth + 1, sb);
        }
    }
}
