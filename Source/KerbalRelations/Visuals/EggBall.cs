using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KerbalRelations.Effects;
using UnityEngine;

namespace KerbalRelations.Visuals
{
    /// <summary>
    /// An egg outside a vessel. Stock puts a Kerbal there; this hides it, switches its walking and
    /// climbing off (see <see cref="Patches.RollingEggPrefix"/>) and lets an egg shaped shell roll and
    /// bounce in its place. The player rolls it with the walking keys, hops with the jump key and puts
    /// it back aboard with the boarding key. Nothing breaks it.
    /// </summary>
    public class EggBall : MonoBehaviour
    {
        /// <summary>The Kerbals outside that are eggs right now.</summary>
        public static readonly HashSet<KerbalEVA> Rolling = new HashSet<KerbalEVA>();

        private const float RollAcceleration = 32f;   // rad/s^2
        private const float PushAcceleration = 2.5f;  // m/s^2, on the ground only
        private const float TopSpeed = 6f;            // m/s, beyond which pushing stops
        private const float HopSpeed = 3.5f;          // m/s
        private const float MaxSpin = 30f;            // rad/s
        private const float BoardRange = 2.5f;        // m from a hatch
        private const float Unbreakable = 1e7f;

        private static readonly MethodInfo RemoveAnchor = AccessTools.Method(typeof(KerbalEVA), "RemoveRBAnchor");
        private static PhysicMaterial _shellMaterial;

        private KerbalEVA _eva;
        private Part _part;
        private string _name;
        private bool _rolling;
        private bool _released;
        private GameObject _shell;

        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<BaseEvent> _events = new List<BaseEvent>();
        private bool _lampWasOn;
        private RigidbodyConstraints _constraints;
        private float _maxSpin;
        private float _angularDrag;
        private float _crashTolerance;
        private double _maxTemp, _skinMaxTemp, _gTolerance, _maxPressure;

        private float _groundedUntil;
        private bool _hop;
        private Part _hatch;
        private float _hatchUntil;
        private float _nextHatchSearch;
        private float _nextHint;

        public void Init(KerbalEVA eva, string eggName)
        {
            _eva = eva;
            _part = eva.part;
            _name = eggName;
        }

        private Rigidbody Body
        {
            get { return _part.rb != null ? _part.rb : _eva.GetComponent<Rigidbody>(); }
        }

        private static PhysicMaterial ShellMaterial
        {
            get
            {
                if (_shellMaterial == null)
                {
                    _shellMaterial = new PhysicMaterial("KR_EggShell")
                    {
                        bounciness = 0.55f,
                        dynamicFriction = 0.6f,
                        staticFriction = 0.6f,
                        bounceCombine = PhysicMaterialCombine.Maximum,
                        frictionCombine = PhysicMaterialCombine.Average
                    };
                }
                return _shellMaterial;
            }
        }

        /// <summary>Takes over from the stock Kerbal, once that has finished setting itself up.</summary>
        private void Begin()
        {
            var body = Body;
            _rolling = true;
            Rolling.Add(_eva);

            // A standing Kerbal is pinned to the spot.
            if (RemoveAnchor != null)
                RemoveAnchor.Invoke(_eva, null);

            foreach (var collider in _part.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled)
                    continue;
                _colliders.Add(collider);
                collider.enabled = false;
            }
            foreach (var renderer in _part.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                _renderers.Add(renderer);
                renderer.enabled = false;
            }
            if (_eva.headLamp != null)
            {
                _lampWasOn = _eva.headLamp.activeSelf;
                _eva.headLamp.SetActive(false);
            }
            foreach (var action in _eva.Events)
            {
                if (!action.guiActive)
                    continue;
                _events.Add(action);
                action.guiActive = false;
            }

            _shell = new GameObject("KR_EggShell") { layer = _part.gameObject.layer };
            _shell.transform.SetParent(_part.transform, false);
            var parentScale = _part.transform.lossyScale;
            // The mesh is two units tall.
            var half = ConfigLoader.EggBallSize / 2f;
            _shell.transform.localScale = new Vector3(
                half / Mathf.Max(0.001f, parentScale.x), half / Mathf.Max(0.001f, parentScale.y), half / Mathf.Max(0.001f, parentScale.z));
            _shell.AddComponent<MeshFilter>().sharedMesh = EggMesh.Get();
            _shell.AddComponent<MeshRenderer>().sharedMaterial = EggMesh.GetMaterial(_name);
            var shape = _shell.AddComponent<MeshCollider>();
            shape.sharedMesh = EggMesh.GetShape();
            shape.convex = true;
            shape.sharedMaterial = ShellMaterial;

            _constraints = body.constraints;
            _maxSpin = body.maxAngularVelocity;
            _angularDrag = body.angularDrag;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.None;
            body.maxAngularVelocity = MaxSpin;
            body.angularDrag = 0.2f;
            body.ResetInertiaTensor();

            _crashTolerance = _part.crashTolerance;
            _maxTemp = _part.maxTemp;
            _skinMaxTemp = _part.skinMaxTemp;
            _gTolerance = _part.gTolerance;
            _maxPressure = _part.maxPressure;
            _part.crashTolerance = Unbreakable;
            _part.maxTemp = Unbreakable;
            _part.skinMaxTemp = Unbreakable;
            _part.gTolerance = Unbreakable;
            _part.maxPressure = Unbreakable;

            Log.Info(string.Format("Egg {0} is rolling: {1} colliders and {2} renderers of the Kerbal switched off", _name, _colliders.Count, _renderers.Count));
        }

        public void FixedUpdate()
        {
            if (_released || _eva == null || _part == null || _part.vessel == null)
                return;
            var vessel = _part.vessel;
            var body = Body;
            if (vessel.packed || body == null)
                return;
            if (!_rolling)
            {
                if (_eva.Ready)
                    Begin();
                return;
            }

            // Stock sets these again when the vessel comes off rails.
            if (body.constraints != RigidbodyConstraints.None)
                body.constraints = RigidbodyConstraints.None;
            for (int i = 0; i < _colliders.Count; ++i)
            {
                if (_colliders[i] != null && _colliders[i].enabled)
                    _colliders[i].enabled = false;
            }

            var hop = _hop;
            _hop = false;
            if (vessel != FlightGlobals.ActiveVessel || !InputLockManager.IsUnlocked(ControlTypes.EVA_INPUT))
                return;

            var up = ((Vector3)vessel.upAxis).normalized;
            var view = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera.transform : null;
            if (view == null)
                return;
            var forward = Vector3.ProjectOnPlane(view.forward, up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.ProjectOnPlane(view.up, up);
            forward.Normalize();
            var right = Vector3.Cross(up, forward);

            var move = Vector3.zero;
            if (GameSettings.EVA_forward.GetKey()) move += forward;
            if (GameSettings.EVA_back.GetKey()) move -= forward;
            if (GameSettings.EVA_right.GetKey()) move += right;
            if (GameSettings.EVA_left.GetKey()) move -= right;

            var grounded = Time.fixedTime < _groundedUntil;
            if (move.sqrMagnitude > 0.01f)
            {
                move.Normalize();
                // A ball rolling towards d on ground with normal n spins about n x d.
                body.AddTorque(Vector3.Cross(up, move) * RollAcceleration, ForceMode.Acceleration);
                if (grounded && Vector3.Dot(body.velocity, move) < TopSpeed)
                    body.AddForce(move * PushAcceleration, ForceMode.Acceleration);
            }
            if (hop && grounded)
            {
                body.AddForce(up * HopSpeed, ForceMode.VelocityChange);
                _groundedUntil = 0f;
            }
        }

        public void Update()
        {
            if (!_rolling || _released || _part == null || _part.vessel == null)
                return;
            var vessel = _part.vessel;
            if (vessel.packed || vessel != FlightGlobals.ActiveVessel || !InputLockManager.IsUnlocked(ControlTypes.EVA_INPUT))
                return;

            if (GameSettings.EVA_Jump.GetKeyDown())
                _hop = true;

            if (Time.time >= _nextHatchSearch)
            {
                _nextHatchSearch = Time.time + 0.4f;
                var near = NearestHatch();
                if (near != null)
                {
                    _hatch = near;
                    _hatchUntil = Time.time + 0.6f;
                }
            }
            if (_hatch == null || Time.time > _hatchUntil || _hatch.protoModuleCrew.Count >= _hatch.CrewCapacity)
                return;

            if (Time.time >= _nextHint)
            {
                _nextHint = Time.time + 1f;
                ScreenMessages.PostScreenMessage("[" + GameSettings.EVA_Board.primary + "]: Put the egg aboard", 1.1f, ScreenMessageStyle.LOWER_CENTER);
            }
            if (GameSettings.EVA_Board.GetKeyUp())
            {
                try
                {
                    // If this works the egg's vessel is gone, and this component with it.
                    _eva.BoardPart(_hatch);
                }
                catch (Exception ex)
                {
                    Log.Error("Could not put egg " + _name + " aboard", ex);
                }
            }
        }

        /// <summary>An egg cannot climb, so a hatch counts as reached from a little way off.</summary>
        private Part NearestHatch()
        {
            Part best = null;
            var bestDistance = BoardRange * BoardRange;
            var position = _part.transform.position;
            var vessels = FlightGlobals.VesselsLoaded;
            for (int v = 0; v < vessels.Count; ++v)
            {
                var vessel = vessels[v];
                if (vessel == null || vessel.isEVA || vessel.packed || (vessel.transform.position - position).sqrMagnitude > 250f * 250f)
                    continue;
                for (int p = 0; p < vessel.parts.Count; ++p)
                {
                    var part = vessel.parts[p];
                    if (part.airlock == null || part.CrewCapacity <= part.protoModuleCrew.Count)
                        continue;
                    var distance = (part.airlock.position - position).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = part;
                    }
                }
            }
            return best;
        }

        public void OnCollisionStay(Collision collision)
        {
            _groundedUntil = Time.fixedTime + 0.15f;
        }

        public void OnTriggerStay(Collider other)
        {
            if (!_rolling || !other.CompareTag("Airlock"))
                return;
            var hatch = FlightGlobals.GetPartUpwardsCached(other.gameObject);
            if (hatch == null)
                return;
            _hatch = hatch;
            _hatchUntil = Time.time + 0.6f;
        }

        public void LateUpdate()
        {
            // Stock switches helmets and visors on and off by itself.
            for (int i = 0; _rolling && !_released && i < _renderers.Count; ++i)
            {
                if (_renderers[i] != null && _renderers[i].enabled)
                    _renderers[i].enabled = false;
            }
        }

        /// <summary>
        /// Gives the stock Kerbal back, for an egg that hatches outside. It is stood upright and left
        /// to find its feet as if it had been dropped there.
        /// </summary>
        public void Release()
        {
            if (_released)
                return;
            _released = true;
            if (!_rolling)
                return;
            _rolling = false;
            Rolling.Remove(_eva);
            if (_eva == null || _part == null)
                return;

            if (_shell != null)
                Destroy(_shell);
            for (int i = 0; i < _colliders.Count; ++i)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = true;
            }
            for (int i = 0; i < _renderers.Count; ++i)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = true;
            }
            for (int i = 0; i < _events.Count; ++i)
                _events[i].guiActive = true;
            if (_eva.headLamp != null)
                _eva.headLamp.SetActive(_lampWasOn);

            _part.crashTolerance = _crashTolerance;
            _part.maxTemp = _maxTemp;
            _part.skinMaxTemp = _skinMaxTemp;
            _part.gTolerance = _gTolerance;
            _part.maxPressure = _maxPressure;

            var body = Body;
            if (body != null)
            {
                body.maxAngularVelocity = _maxSpin;
                body.angularDrag = _angularDrag;
                body.angularVelocity = Vector3.zero;
                body.constraints = _constraints;
                body.ResetInertiaTensor();
            }
            if (_part.vessel != null)
            {
                var up = ((Vector3)_part.vessel.upAxis).normalized;
                _part.transform.rotation = Quaternion.FromToRotation(_part.transform.up, up) * _part.transform.rotation;
            }
            try
            {
                _eva.fsm.StartFSM(_eva.st_idle_fl);
            }
            catch (Exception ex)
            {
                Log.Error("Could not put the Kerbal that hatched from " + _name + " back on its feet", ex);
            }
        }

        public void OnDestroy()
        {
            // Without Release the whole Kerbal is going away (boarded, or the scene ends).
            if (_eva != null)
                Rolling.Remove(_eva);
        }
    }
}
