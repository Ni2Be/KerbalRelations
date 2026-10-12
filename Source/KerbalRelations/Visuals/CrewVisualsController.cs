using System;
using KerbalRelations.Core;
using KerbalRelations.Effects;
using UnityEngine;

namespace KerbalRelations.Visuals
{
    /// <summary>
    /// Keeps the models of eggs and children in step with their life stage: the ones sitting in a
    /// cabin, and the ones outside.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CrewVisualsController : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private float _next;

        public void Start()
        {
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
        }

        public void OnDestroy()
        {
            GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
        }

        /// <summary>Stock only lets crew act outside a vessel; see <see cref="EvaAccess"/>.</summary>
        private static void OnCrewOnEva(GameEvents.FromToAction<Part, Part> action)
        {
            try
            {
                var crew = action.to != null && action.to.protoModuleCrew.Count > 0 ? action.to.protoModuleCrew[0] : null;
                if (crew != null && crew.type == ProtoCrewMember.KerbalType.Tourist && EvaAccess.MayGoOutside(crew))
                    crew.type = ProtoCrewMember.KerbalType.Crew;
            }
            catch (Exception ex)
            {
                Log.Error("Preparing an egg or child for going outside failed", ex);
            }
        }

        public void Update()
        {
            if (!FlightGlobals.ready)
                return;
            // Kerbals outside are looked at every frame, so that nobody steps out as an adult first.
            var cabins = Time.unscaledTime >= _next;
            if (cabins)
                _next = Time.unscaledTime + Interval;

            try
            {
                Refresh(cabins);
            }
            catch (Exception ex)
            {
                Log.Error("Updating egg and child models failed", ex);
            }
        }

        private static void Refresh(bool cabins)
        {
            var scenario = RelationsScenario.Instance;
            var active = scenario != null && scenario.Active;

            var vessels = FlightGlobals.VesselsLoaded;
            for (int v = 0; v < vessels.Count; ++v)
            {
                var vessel = vessels[v];
                if (vessel == null)
                    continue;
                if (vessel.isEVA)
                {
                    RefreshOutside(vessel, active ? scenario.Data : null);
                    continue;
                }
                if (!cabins)
                    continue;
                for (int p = 0; p < vessel.parts.Count; ++p)
                {
                    var crew = vessel.parts[p].protoModuleCrew;
                    for (int c = 0; c < crew.Count; ++c)
                    {
                        // KerbalRef is the model sitting in the cabin; it is recreated on crew changes.
                        var kerbal = crew[c].KerbalRef;
                        if (kerbal == null)
                            continue;
                        var state = active ? scenario.Data.Get(crew[c].name) : null;
                        var stage = state != null ? state.Stage : LifeStage.Adult;
                        var visual = kerbal.GetComponent<StageVisual>();
                        if (stage == LifeStage.Adult)
                        {
                            if (visual != null)
                                Destroy(visual);
                            continue;
                        }
                        if (visual == null)
                            visual = kerbal.gameObject.AddComponent<StageVisual>();
                        visual.Apply(kerbal, stage, crew[c].name);
                    }
                }
            }
        }

        /// <summary>A child outside is a small Kerbal; an egg outside rolls.</summary>
        private static void RefreshOutside(Vessel vessel, RelationsData data)
        {
            // Not before stock has finished setting the Kerbal up.
            var eva = vessel.evaController;
            if (eva == null || !eva.Ready || eva.part == null || eva.part.protoModuleCrew.Count == 0)
                return;
            var crew = eva.part.protoModuleCrew[0];
            var state = data != null ? data.Get(crew.name) : null;
            var stage = state != null ? state.Stage : LifeStage.Adult;

            var ball = eva.GetComponent<EggBall>();
            if (stage != LifeStage.Egg && ball != null)
            {
                ball.Release();
                Destroy(ball);
            }
            var visual = eva.GetComponent<StageVisual>();
            if (stage != LifeStage.Child && visual != null)
                Destroy(visual);

            if (stage == LifeStage.Egg && ball == null)
            {
                eva.gameObject.AddComponent<EggBall>().Init(eva, crew.name);
            }
            else if (stage == LifeStage.Child)
            {
                if (visual == null)
                    visual = eva.gameObject.AddComponent<StageVisual>();
                visual.ApplyEva(eva, crew);
            }
        }
    }
}
