using KerbalRelations.Core;
using UnityEngine;

namespace KerbalRelations.Effects
{
    /// <summary>Scales how fast Kerbals on EVA move with their happiness.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EvaSpeedController : MonoBehaviour
    {
        private const float Interval = 1f;
        private float _next;

        public void Update()
        {
            if (Time.time < _next || !FlightGlobals.ready)
                return;
            _next = Time.time + Interval;

            var vessels = FlightGlobals.VesselsLoaded;
            for (int i = 0; i < vessels.Count; ++i)
            {
                var vessel = vessels[i];
                if (vessel == null || !vessel.isEVA)
                    continue;
                var eva = vessel.evaController;
                if (eva == null)
                    continue;
                var crew = vessel.GetVesselCrew();
                if (crew.Count == 0)
                    continue;

                var baseSpeeds = eva.GetComponent<EvaBaseSpeeds>();
                if (baseSpeeds == null)
                {
                    baseSpeeds = eva.gameObject.AddComponent<EvaBaseSpeeds>();
                    baseSpeeds.Capture(eva);
                }
                // GetMultiplier is 1 when the mod is switched off, which restores the stock speeds.
                baseSpeeds.Apply(eva, (float)KerbalRelationsAPI.GetMultiplier(crew[0].name, EffectRegistry.RunSpeed));
            }
        }
    }

    /// <summary>Remembers the unmodified speeds of one EVA Kerbal so the multiplier never compounds.</summary>
    public class EvaBaseSpeeds : MonoBehaviour
    {
        private float _walk, _run, _strafe, _swim, _ladder, _bound;
        private float _applied = 1f;

        public void Capture(KerbalEVA eva)
        {
            _walk = eva.walkSpeed;
            _run = eva.runSpeed;
            _strafe = eva.strafeSpeed;
            _swim = eva.swimSpeed;
            _ladder = eva.ladderClimbSpeed;
            _bound = eva.boundSpeed;
        }

        public void Apply(KerbalEVA eva, float multiplier)
        {
            if (Mathf.Approximately(multiplier, _applied))
                return;
            _applied = multiplier;
            eva.walkSpeed = _walk * multiplier;
            eva.runSpeed = _run * multiplier;
            eva.strafeSpeed = _strafe * multiplier;
            eva.swimSpeed = _swim * multiplier;
            eva.ladderClimbSpeed = _ladder * multiplier;
            eva.boundSpeed = _bound * multiplier;
        }
    }
}
