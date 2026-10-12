using System;
using System.Collections.Generic;
using System.Text;
using KerbalRelations.Core;
using KerbalRelations.Effects;
using KerbalRelations.Missions;
using KerbalRelations.Visuals;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalRelations.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RelationsWindowFlight : RelationsWindow
    {
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RelationsWindowSpaceCentre : RelationsWindow
    {
    }

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class RelationsWindowTrackingStation : RelationsWindow
    {
    }

    /// <summary>
    /// The relationship graph: every Kerbal is a node, every relationship an edge, and a force-directed
    /// layout pulls Kerbals with strong bonds close together. Kerbals who never met are not connected.
    /// </summary>
    public class RelationsWindow : MonoBehaviour
    {
        private const float GraphWidth = 640f;
        private const float GraphHeight = 430f;
        private const float RibbonPanelHeight = 76f;
        private const float RibbonGap = 4f;
        private const int MaxNodes = 80;
        private const string LockId = "KerbalRelationsWindow";
        private const int WindowId = 0x4B52454C;
        private const int ViewsWindowId = WindowId + 1;
        private const int MenuWindowId = WindowId + 2;
        private const float ViewsWidth = 300f;
        private const float MenuWidth = 250f;

        /// <summary>Which Kerbals the graph shows.</summary>
        private enum ViewKind
        {
            Everyone,
            /// <summary>The vessel being flown, with the vessels next to it.</summary>
            Vessel,
            /// <summary>The KSC.</summary>
            Home,
            /// <summary>Everybody on and around one body.</summary>
            Body
        }

        private class ViewOption
        {
            public ViewKind Kind;
            public string Body;
            public string Label;
            public int Count;
        }

        // Static so the window keeps its place and state across scene changes.
        private static bool _visible;
        private static Rect _window = new Rect(220f, 90f, GraphWidth + 24f, 100f);
        private static ViewKind _viewKind = ViewKind.Vessel;
        private static string _viewBody;

        private ApplicationLauncherButton _button;
        private readonly GraphLayout _layout = new GraphLayout { Width = GraphWidth, Height = GraphHeight - 16d };
        private readonly List<string> _nodes = new List<string>();
        private readonly HashSet<string> _nodeSet = new HashSet<string>();
        private readonly List<GraphEdge> _edges = new List<GraphEdge>();
        private readonly List<Relationship> _edgeRelationships = new List<Relationship>();
        private float _nextRebuild;
        private string _dragging;
        private string _selected;
        private bool _locked;
        private string _tooltip;
        private float[] _skinSliders;

        // The list of views and the right-click menu are small windows of their own, so that they
        // lie on top of the graph and get the clicks.
        private readonly List<ViewOption> _views = new List<ViewOption>();
        private bool _viewsOpen;
        private float _viewsClosedAt;
        private Vector2 _viewsAnchor;
        private Rect _viewsRect;
        private string _menuKerbal;
        private Vector2 _menuAnchor;
        private Rect _menuRect;

        private GUIStyle _nameStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _tipStyle;
        private GUIStyle _centerStyle;
        private GUIStyle _popupStyle;
        private GUIStyle _optionStyle;

        public void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            if (ApplicationLauncher.Ready)
                AddButton();
        }

        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            RemoveButton(HighLogic.LoadedScene);
            SetLock(false);
        }

        private void AddButton()
        {
            if (_button != null || ApplicationLauncher.Instance == null)
                return;
            const ApplicationLauncher.AppScenes scenes = ApplicationLauncher.AppScenes.FLIGHT
                | ApplicationLauncher.AppScenes.MAPVIEW
                | ApplicationLauncher.AppScenes.SPACECENTER
                | ApplicationLauncher.AppScenes.TRACKSTATION;
            _button = ApplicationLauncher.Instance.AddModApplication(
                () => _visible = true, () => _visible = false, null, null, null, null, scenes, GraphTextures.Toolbar);
            if (_visible)
                _button.SetTrue(false);
        }

        private void RemoveButton(GameScenes scene)
        {
            if (_button == null)
                return;
            if (ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void Close()
        {
            _visible = false;
            if (_button != null)
                _button.SetFalse(false);
        }

        public void OnGUI()
        {
            var scenario = RelationsScenario.Instance;
            if (!_visible || scenario == null)
            {
                SetLock(false);
                return;
            }

            GUI.skin = HighLogic.Skin;
            InitStyles();

            // A click anywhere else closes the list of views and the menu.
            var e = Event.current;
            if (e.type == EventType.MouseDown)
            {
                if (_viewsOpen && !_viewsRect.Contains(e.mousePosition))
                {
                    _viewsOpen = false;
                    _viewsClosedAt = Time.unscaledTime;
                }
                if (_menuKerbal != null && !_menuRect.Contains(e.mousePosition))
                    _menuKerbal = null;
            }

            _window.x = Mathf.Clamp(_window.x, 40f - _window.width, Screen.width - 40f);
            _window.y = Mathf.Clamp(_window.y, 0f, Screen.height - 30f);
            _window = GUILayout.Window(WindowId, _window, id => DrawWindow(scenario), "Kerbal Relations");

            if (_viewsOpen)
            {
                _viewsRect = GUILayout.Window(ViewsWindowId, new Rect(_viewsAnchor.x, _viewsAnchor.y, ViewsWidth, 10f),
                    id => DrawViews(), GUIContent.none, _popupStyle);
                GUI.BringWindowToFront(ViewsWindowId);
            }
            if (_menuKerbal != null)
            {
                _menuRect = GUILayout.Window(MenuWindowId,
                    new Rect(Mathf.Min(_menuAnchor.x, Screen.width - MenuWidth - 20f), _menuAnchor.y, MenuWidth, 10f),
                    id => DrawMenu(scenario), GUIContent.none, _popupStyle);
                GUI.BringWindowToFront(MenuWindowId);
            }

            // Keep clicks and the scroll wheel from reaching the game underneath.
            var mouse = Input.mousePosition;
            mouse.y = Screen.height - mouse.y;
            SetLock(_window.Contains(mouse) || (_viewsOpen && _viewsRect.Contains(mouse)) || (_menuKerbal != null && _menuRect.Contains(mouse)));
        }

        private void SetLock(bool locked)
        {
            if (locked == _locked)
                return;
            _locked = locked;
            if (locked)
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.KSC_ALL | ControlTypes.TRACKINGSTATION_ALL, LockId);
            else
                InputLockManager.RemoveControlLock(LockId);
        }

        private void InitStyles()
        {
            if (_nameStyle != null)
                return;
            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 11,
                wordWrap = false,
                richText = true,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _nameStyle.normal.textColor = Color.white;
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, wordWrap = true };
            _centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _tipStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };
            _tipStyle.normal.textColor = Color.white;
            _popupStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 6, 6) };
            _optionStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true };
        }

        private void DrawWindow(RelationsScenario scenario)
        {
            var inFlight = HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null;

            GUILayout.BeginHorizontal();
            // The click that closed the list by landing outside it must not open it again.
            if (GUILayout.Button("Show: " + CurrentViewLabel(inFlight), GUILayout.Width(ViewsWidth)) && Time.unscaledTime - _viewsClosedAt > 0.3f)
                _viewsOpen = !_viewsOpen;
            if (Event.current.type == EventType.Repaint)
            {
                var button = GUILayoutUtility.GetLastRect();
                _viewsAnchor = GUIUtility.GUIToScreenPoint(new Vector2(button.x, button.yMax));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(60f)))
                Close();
            GUILayout.EndHorizontal();

            _tooltip = null;
            Vector2 tipAnchor = Vector2.zero;

            if (!scenario.Active)
            {
                GUILayout.Label("Kerbal Relations is switched off for this save (Difficulty Settings).", _centerStyle,
                    GUILayout.Width(GraphWidth), GUILayout.Height(80f));
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(GraphWidth, GraphHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                DrawGraph(rect, scenario, EffectiveView(inFlight));
                var rack = GUILayoutUtility.GetRect(GraphWidth, RibbonPanelHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                DrawRibbons(rack, scenario);
                tipAnchor = Event.current.mousePosition;

                GUILayout.Label("Ring colour: <color=#e6402f>unhappy</color> · <color=#f2cc40>70%</color> · "
                    + "<color=#73d95a>100%</color> · <color=#4dd9e6>150%</color> · <color=#bf8cff>200%</color>"
                    + "     Lines: thicker = stronger, <color=#ff73b3>couple</color>, <color=#73b3ff>family</color>"
                    + "     Drag Kerbals to rearrange, right-click for their vessel.", _smallStyle);

                if (scenario.Config.Debug)
                    DrawDebug(scenario);
            }

            if (_tooltip != null && Event.current.type == EventType.Repaint)
                DrawTooltip(_tooltip, tipAnchor);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        /// <summary>Outside a flight there is no "this vessel".</summary>
        private static ViewKind EffectiveView(bool inFlight)
        {
            return _viewKind == ViewKind.Vessel && !inFlight ? ViewKind.Everyone : _viewKind;
        }

        private static string BodyName(CelestialBody body)
        {
            return Localizer.Format("<<1>>", body.displayName);
        }

        private string CurrentViewLabel(bool inFlight)
        {
            var kind = EffectiveView(inFlight);
            for (int i = 0; i < _views.Count; ++i)
            {
                if (_views[i].Kind == kind && (kind != ViewKind.Body || _views[i].Body == _viewBody))
                    return _views[i].Label + " (" + _views[i].Count + ")";
            }
            // Nobody is there any more, so it is not in the list.
            if (kind == ViewKind.Body)
            {
                var body = FlightGlobals.GetBodyByName(_viewBody);
                return (body != null ? BodyName(body) : _viewBody) + " (0)";
            }
            return kind == ViewKind.Home ? "At the KSC (0)" : kind == ViewKind.Vessel ? "This vessel (0)" : "Everyone (0)";
        }

        /// <summary>The views there are right now: everyone, and every place that has Kerbals.</summary>
        private void RebuildViews(RelationsScenario scenario, Vessel active)
        {
            var data = scenario.Data;
            var groups = scenario.Groups;
            var roster = HighLogic.CurrentGame.CrewRoster;
            int everyone = 0, here = 0, home = 0;
            var perBody = new Dictionary<string, int>();
            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                var vessels = (List<Vessel>)group.Tag;
                var count = 0;
                for (int i = 0; i < group.Members.Count; ++i)
                {
                    if (data.Get(group.Members[i]) != null && roster[group.Members[i]] != null)
                        ++count;
                }
                everyone += count;
                if (group.AtHome)
                {
                    home += count;
                    continue;
                }
                if (active != null && vessels.Contains(active))
                    here += count;
                if (vessels.Count == 0 || vessels[0].mainBody == null)
                    continue;
                int soFar;
                perBody.TryGetValue(vessels[0].mainBody.bodyName, out soFar);
                perBody[vessels[0].mainBody.bodyName] = soFar + count;
            }

            _views.Clear();
            _views.Add(new ViewOption { Kind = ViewKind.Everyone, Label = "Everyone", Count = everyone });
            if (active != null && here > 0)
                _views.Add(new ViewOption { Kind = ViewKind.Vessel, Label = "This vessel", Count = here });
            if (home > 0)
                _views.Add(new ViewOption { Kind = ViewKind.Home, Label = "At the KSC", Count = home });
            // In the order of the solar system rather than by head count, so that the list holds still.
            var bodies = FlightGlobals.Bodies;
            for (int b = 0; bodies != null && b < bodies.Count; ++b)
            {
                int count;
                if (perBody.TryGetValue(bodies[b].bodyName, out count) && count > 0)
                    _views.Add(new ViewOption { Kind = ViewKind.Body, Body = bodies[b].bodyName, Label = BodyName(bodies[b]), Count = count });
            }
        }

        private void DrawViews()
        {
            Backdrop(_viewsRect);
            for (int i = 0; i < _views.Count; ++i)
            {
                var view = _views[i];
                var current = view.Kind == _viewKind && (view.Kind != ViewKind.Body || view.Body == _viewBody);
                var label = view.Label + " (" + view.Count + ")";
                if (!GUILayout.Button(current ? "<b>" + label + "</b>" : label, _optionStyle))
                    continue;
                _viewKind = view.Kind;
                _viewBody = view.Body;
                _viewsOpen = false;
                _nextRebuild = 0f;
            }
        }

        /// <summary>The stock box is see-through; over the graph that is hard to read.</summary>
        private static void Backdrop(Rect window)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            GUI.color = new Color(0.07f, 0.08f, 0.09f, 0.97f);
            GUI.DrawTexture(new Rect(0f, 0f, window.width, window.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>The vessel a Kerbal is aboard (the Kerbal's own, when outside); null at the KSC.</summary>
        private static Vessel VesselOf(RelationsScenario scenario, KerbalState k)
        {
            var groups = scenario.Groups;
            if (k == null || k.GroupIndex < 0 || k.GroupIndex >= groups.Count)
                return null;
            var vessels = groups[k.GroupIndex].Tag as List<Vessel>;
            return vessels != null && k.Place >= 0 && k.Place < vessels.Count ? vessels[k.Place] : null;
        }

        private static bool IsOutside(Vessel vessel)
        {
            return vessel.isEVA || vessel.vesselType == VesselType.EVA;
        }

        /// <summary>"Aboard Hab One, on Minmus"; null when that is not known.</summary>
        private static string Whereabouts(RelationsScenario scenario, KerbalState k)
        {
            if (k.AtHome)
                return "At the KSC";
            var vessel = VesselOf(scenario, k);
            if (vessel == null || vessel.mainBody == null)
                return null;
            var body = BodyName(vessel.mainBody);
            string place;
            switch (vessel.situation)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.PRELAUNCH:
                    place = "on " + body;
                    break;
                case Vessel.Situations.SPLASHED:
                    place = "in the water on " + body;
                    break;
                case Vessel.Situations.FLYING:
                    place = "flying over " + body;
                    break;
                case Vessel.Situations.SUB_ORBITAL:
                    place = "on the way up or down at " + body;
                    break;
                case Vessel.Situations.ESCAPING:
                    place = "leaving " + body;
                    break;
                default:
                    place = "in orbit around " + body;
                    break;
            }
            return (IsOutside(vessel) ? "Outside, " : "Aboard " + vessel.vesselName + ", ") + place;
        }

        /// <summary>The menu that opens on a right-click on a Kerbal.</summary>
        private void DrawMenu(RelationsScenario scenario)
        {
            Backdrop(_menuRect);
            var k = scenario.Data.Get(_menuKerbal);
            if (k == null)
            {
                _menuKerbal = null;
                return;
            }
            var width = GUILayout.Width(MenuWidth - 12f);
            GUILayout.Label("<b>" + k.Name + "</b>\n" + (Whereabouts(scenario, k) ?? "Whereabouts unknown"), _smallStyle, width);

            var vessel = VesselOf(scenario, k);
            if (vessel == null)
                return;
            if (HighLogic.LoadedSceneIsFlight && vessel == FlightGlobals.ActiveVessel)
            {
                GUILayout.Label("<color=#999999>You are flying this vessel.</color>", _smallStyle, width);
                return;
            }
            if (GUILayout.Button("Go to " + (IsOutside(vessel) ? RelationsSim.ShortName(k.Name) : vessel.vesselName), width))
            {
                _menuKerbal = null;
                GoTo(vessel);
            }
        }

        /// <summary>Switches to a vessel the way the map view and the tracking station do.</summary>
        private static void GoTo(Vessel vessel)
        {
            try
            {
                if (HighLogic.LoadedSceneIsFlight)
                {
                    var status = FlightGlobals.ClearToSave();
                    if (status != ClearToSaveStatus.CLEAR)
                    {
                        ScreenMessages.PostScreenMessage("Cannot leave this vessel " + WhyNot(status), 5f, ScreenMessageStyle.UPPER_CENTER);
                        return;
                    }
                    FlightGlobals.SetActiveVessel(vessel);
                    return;
                }
                var index = FlightGlobals.Vessels.IndexOf(vessel);
                if (index < 0)
                    return;
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                FlightDriver.StartAndFocusVessel("persistent", index);
            }
            catch (Exception ex)
            {
                Log.Error("Could not go to " + vessel.vesselName, ex);
            }
        }

        private static string WhyNot(ClearToSaveStatus status)
        {
            switch (status)
            {
                case ClearToSaveStatus.NOT_IN_ATMOSPHERE:
                    return "while it is flying through an atmosphere";
                case ClearToSaveStatus.NOT_UNDER_ACCELERATION:
                    return "while it is accelerating";
                case ClearToSaveStatus.NOT_WHILE_MOVING_OVER_SURFACE:
                    return "while it is moving over the surface";
                case ClearToSaveStatus.NOT_WHILE_ABOUT_TO_CRASH:
                    return "while it is about to crash";
                case ClearToSaveStatus.NOT_WHILE_ON_A_LADDER:
                    return "while on a ladder";
                case ClearToSaveStatus.NOT_WHILE_THROTTLED_UP:
                    return "while the throttle is up";
                default:
                    return "right now";
            }
        }

        private void RebuildNodes(RelationsScenario scenario, ViewKind view)
        {
            _nodes.Clear();
            _nodeSet.Clear();
            var data = scenario.Data;
            var groups = scenario.Groups;
            var roster = HighLogic.CurrentGame.CrewRoster;
            var active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            RebuildViews(scenario, active);

            for (int g = 0; g < groups.Count; ++g)
            {
                var group = groups[g];
                var vessels = (List<Vessel>)group.Tag;
                if (view == ViewKind.Vessel && (active == null || !vessels.Contains(active)))
                    continue;
                if (view == ViewKind.Home && !group.AtHome)
                    continue;
                if (view == ViewKind.Body
                    && (group.AtHome || vessels.Count == 0 || vessels[0].mainBody == null || vessels[0].mainBody.bodyName != _viewBody))
                    continue;

                for (int i = 0; i < group.Members.Count && _nodes.Count < MaxNodes; ++i)
                {
                    var name = group.Members[i];
                    var state = data.Get(name);
                    if (state == null || roster[name] == null)
                        continue;
                    // The KSC is crowded; unless it is what was asked for, only show the Kerbals there
                    // who know somebody.
                    if (group.AtHome && view != ViewKind.Home && data.RelationshipsOf(name).Count == 0)
                        continue;
                    if (_nodeSet.Add(name))
                        _nodes.Add(name);
                }
            }

            _layout.Sync(_nodeSet);
            _edges.Clear();
            _edgeRelationships.Clear();
            foreach (var rel in data.Relationships.Values)
            {
                if (!_nodeSet.Contains(rel.A) || !_nodeSet.Contains(rel.B))
                    continue;
                _edges.Add(new GraphEdge { A = rel.A, B = rel.B, Strength = rel.Strength });
                _edgeRelationships.Add(rel);
            }
            if (_selected != null && !_nodeSet.Contains(_selected))
                _selected = null;
        }

        private static float NodeRadius(LifeStage stage)
        {
            return stage == LifeStage.Egg ? 20f : stage == LifeStage.Child ? 22f : 27f;
        }

        private void DrawGraph(Rect rect, RelationsScenario scenario, ViewKind view)
        {
            var e = Event.current;
            var data = scenario.Data;

            if (e.type == EventType.Repaint && Time.unscaledTime >= _nextRebuild)
            {
                RebuildNodes(scenario, view);
                _nextRebuild = Time.unscaledTime + 1f;
            }

            GUI.Box(rect, GUIContent.none);
            if (_nodes.Count == 0)
            {
                GUI.Label(rect, scenario.Groups.Count == 0 ? "Gathering data..." : "No Kerbals to show here yet.", _centerStyle);
                return;
            }

            // Leave room under the nodes for their name labels.
            var centre = new Vector2(rect.center.x, rect.center.y - 8f);
            if (e.type == EventType.Repaint)
            {
                for (int i = 0; i < 3; ++i)
                    _layout.Step(_edges);
            }

            var mouse = e.mousePosition;
            string hover = null;
            if (rect.Contains(mouse) || _dragging != null)
            {
                var best = float.MaxValue;
                for (int i = 0; i < _nodes.Count; ++i)
                {
                    GraphNode node;
                    var state = data.Get(_nodes[i]);
                    if (state == null || !_layout.Nodes.TryGetValue(_nodes[i], out node))
                        continue;
                    var d = (mouse - Position(centre, node)).magnitude;
                    if (d <= NodeRadius(state.Stage) && d < best)
                    {
                        best = d;
                        hover = _nodes[i];
                    }
                }
            }

            HandleInput(e, hover, mouse, centre);
            if (e.type != EventType.Repaint)
                return;

            // Edges underneath.
            Relationship hoverEdge = null;
            for (int i = 0; i < _edges.Count; ++i)
            {
                GraphNode a, b;
                if (!_layout.Nodes.TryGetValue(_edges[i].A, out a) || !_layout.Nodes.TryGetValue(_edges[i].B, out b))
                    continue;
                var rel = _edgeRelationships[i];
                var pa = Position(centre, a);
                var pb = Position(centre, b);
                var over = hover == null && _dragging == null && DistanceToSegment(mouse, pa, pb) < 6f;
                if (over)
                    hoverEdge = rel;
                DrawLine(pa, pb, EdgeColour(rel, over), 1.5f + 4f * (float)rel.Strength / 100f);
            }

            var roster = HighLogic.CurrentGame.CrewRoster;
            for (int i = 0; i < _nodes.Count; ++i)
            {
                GraphNode node;
                var state = data.Get(_nodes[i]);
                var pcm = roster[_nodes[i]];
                if (state == null || pcm == null || !_layout.Nodes.TryGetValue(_nodes[i], out node))
                    continue;
                DrawNode(Position(centre, node), state, pcm, _nodes[i] == hover, _nodes[i] == _selected);
            }
            GUI.color = Color.white;

            if (_dragging == null)
            {
                if (hover != null)
                    _tooltip = KerbalTooltip(scenario, data.Get(hover), roster[hover]);
                else if (hoverEdge != null)
                    _tooltip = EdgeTooltip(scenario, hoverEdge);
            }
        }

        /// <summary>The ribbon rack of the Kerbal that was clicked last; hover over a ribbon to read it.</summary>
        private void DrawRibbons(Rect rect, RelationsScenario scenario)
        {
            var k = scenario.Data.Get(_selected);
            if (k == null)
            {
                GUI.Label(rect, "<color=#999999>Click a Kerbal to see their ribbons.</color>", _smallStyle);
                return;
            }
            const float labelWidth = 118f;
            GUI.Label(new Rect(rect.x, rect.y, labelWidth, rect.height),
                string.Format("<b>{0}</b>\n{1} {2}", RelationsSim.ShortName(k.Name), k.Ribbons.Count, k.Ribbons.Count == 1 ? "ribbon" : "ribbons"), _smallStyle);
            if (k.Ribbons.Count == 0)
            {
                GUI.Label(new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height),
                    k.Stage == LifeStage.Egg || k.Visitor
                        ? "<color=#999999>Eggs and guests do not collect ribbons.</color>"
                        : "<color=#999999>No ribbons yet. Travel, friends and family all earn them.</color>", _smallStyle);
                return;
            }

            var config = scenario.Config;
            var now = RelationsScenario.Now;
            var mouse = Event.current.mousePosition;
            var pitchX = RibbonArt.Width + RibbonGap;
            var pitchY = RibbonArt.Height + RibbonGap;
            var perRow = Mathf.Max(1, Mathf.FloorToInt((rect.width - labelWidth + RibbonGap) / pitchX));
            var rows = Mathf.Max(1, Mathf.FloorToInt((rect.height + RibbonGap) / pitchY));
            var room = perRow * rows;
            // The newest ribbons are the ones that stay when there are more than fit.
            var first = Mathf.Max(0, k.Ribbons.Count - room);
            for (int i = first; i < k.Ribbons.Count; ++i)
            {
                var slot = i - first;
                var def = scenario.Sim.Defs.ResolveRibbon(k.Ribbons[i].Id);
                var r = new Rect(rect.x + labelWidth + (slot % perRow) * pitchX, rect.y + 2f + (slot / perRow) * pitchY,
                    RibbonArt.Width, RibbonArt.Height);
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(r, GraphTextures.GetMedal(def));
                    var picture = GraphTextures.GetMedalPicture(def);
                    if (picture != null)
                        GUI.DrawTexture(new Rect(r.x + RibbonArt.GlyphX, r.y + RibbonArt.GlyphY, RibbonArt.GlyphSize, RibbonArt.GlyphSize), picture);
                }
                if (r.Contains(mouse) && _dragging == null)
                {
                    _tooltip = string.Format("<b>{0}</b>\n{1}\n<color=#999999>Earned {2} ago</color>", def.Name, def.Description,
                        Duration(config, now - k.Ribbons[i].UT));
                }
            }
        }

        private static Vector2 Position(Vector2 centre, GraphNode node)
        {
            return new Vector2(centre.x + (float)node.X, centre.y + (float)node.Y);
        }

        private void HandleInput(Event e, string hover, Vector2 mouse, Vector2 centre)
        {
            GraphNode node;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hover != null && _layout.Nodes.TryGetValue(hover, out node))
                    {
                        _dragging = hover;
                        _selected = hover;
                        node.Pinned = true;
                        e.Use();
                    }
                    else if (e.button == 1 && hover != null)
                    {
                        _selected = hover;
                        _menuKerbal = hover;
                        _menuAnchor = GUIUtility.GUIToScreenPoint(mouse);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_dragging != null && _layout.Nodes.TryGetValue(_dragging, out node))
                    {
                        node.X = Mathf.Clamp(mouse.x - centre.x, -(float)_layout.Width / 2f + 30f, (float)_layout.Width / 2f - 30f);
                        node.Y = Mathf.Clamp(mouse.y - centre.y, -(float)_layout.Height / 2f + 30f, (float)_layout.Height / 2f - 30f);
                        e.Use();
                    }
                    break;
            }

            // rawType also catches a release outside the window.
            if (_dragging != null && e.rawType == EventType.MouseUp)
            {
                if (_layout.Nodes.TryGetValue(_dragging, out node))
                    node.Pinned = false;
                _dragging = null;
            }
        }

        private void DrawNode(Vector2 pos, KerbalState state, ProtoCrewMember pcm, bool hover, bool selected)
        {
            var radius = NodeRadius(state.Stage) * (hover ? 1.08f : 1f);
            var outer = new Rect(pos.x - radius, pos.y - radius, radius * 2f, radius * 2f);

            if (selected)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                var halo = radius + 4f;
                GUI.DrawTexture(new Rect(pos.x - halo, pos.y - halo, halo * 2f, halo * 2f), GraphTextures.Ring);
            }

            GUI.color = new Color(0.1f, 0.12f, 0.14f, 0.95f);
            GUI.DrawTexture(outer, GraphTextures.Disc);

            // Small enough that the corners of the square portrait stay inside the circle.
            var inner = radius * (state.Stage == LifeStage.Egg ? 0.8f : 0.68f);
            var icon = GraphTextures.GetIcon(pcm, state.Stage);
            GUI.color = Color.white;
            if (icon != null)
                GUI.DrawTexture(new Rect(pos.x - inner, pos.y - inner, inner * 2f, inner * 2f), icon, ScaleMode.ScaleToFit, true);

            GUI.color = state.Stage == LifeStage.Egg ? new Color(0.75f, 0.75f, 0.7f) : HappinessColour(state.Happiness);
            GUI.DrawTexture(outer, GraphTextures.Ring);

            GUI.color = Color.white;
            var label = RelationsSim.ShortName(state.Name);
            if (state.Stage == LifeStage.Egg)
                label += " <color=#bbbbbb>(egg)</color>";
            else
                label += string.Format(" <color=#{0}>{1:0}%</color>", ColorUtility.ToHtmlStringRGB(HappinessColour(state.Happiness)), state.Happiness);
            GUI.Label(new Rect(pos.x - 80f, pos.y + radius + 1f, 160f, 16f), label, _nameStyle);
        }

        private static Color HappinessColour(double happiness)
        {
            var h = (float)happiness;
            var red = new Color(0.9f, 0.25f, 0.18f);
            var yellow = new Color(0.95f, 0.8f, 0.25f);
            var green = new Color(0.45f, 0.85f, 0.35f);
            var cyan = new Color(0.3f, 0.85f, 0.9f);
            var violet = new Color(0.75f, 0.55f, 1f);
            if (h < 70f)
                return Color.Lerp(red, yellow, h / 70f);
            if (h < 100f)
                return Color.Lerp(yellow, green, (h - 70f) / 30f);
            if (h < 150f)
                return Color.Lerp(green, cyan, (h - 100f) / 50f);
            return Color.Lerp(cyan, violet, (h - 150f) / 50f);
        }

        private static Color EdgeColour(Relationship rel, bool highlight)
        {
            Color colour;
            if (rel.IsCouple)
                colour = new Color(1f, 0.45f, 0.7f, 0.95f);
            else if (rel.Family)
                colour = new Color(0.45f, 0.7f, 1f, 0.9f);
            else
                colour = Color.Lerp(new Color(0.6f, 0.6f, 0.6f, 0.55f), new Color(0.5f, 0.9f, 0.4f, 0.95f), (float)rel.Strength / 100f);
            return highlight ? Color.Lerp(colour, Color.white, 0.5f) : colour;
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color colour, float width)
        {
            var length = (b - a).magnitude;
            if (length < 0.5f)
                return;
            var matrix = GUI.matrix;
            GUI.color = colour;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 0.001f)
                return (p - a).magnitude;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
            return (p - (a + ab * t)).magnitude;
        }

        private void DrawTooltip(string text, Vector2 anchor)
        {
            const float width = 300f;
            var content = new GUIContent(text);
            var height = _tipStyle.CalcHeight(content, width);
            var x = anchor.x + 18f;
            if (x + width > _window.width - 4f)
                x = anchor.x - width - 12f;
            var y = Mathf.Clamp(anchor.y + 12f, 4f, Mathf.Max(4f, _window.height - height - 4f));
            var rect = new Rect(Mathf.Max(4f, x), y, width, height);

            GUI.color = new Color(0f, 0f, 0f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, content, _tipStyle);
        }

        private static string Duration(SimConfig config, double seconds)
        {
            seconds = Math.Max(0d, seconds);
            var years = Math.Floor(seconds / config.SecondsPerYear);
            var days = Math.Floor((seconds - years * config.SecondsPerYear) / config.SecondsPerDay);
            if (years >= 1d)
                return string.Format("{0:0}y {1:0}d", years, days);
            var hours = Math.Floor((seconds - days * config.SecondsPerDay) / 3600d);
            if (days >= 1d)
                return string.Format("{0:0}d {1:0}h", days, hours);
            return hours >= 1d ? string.Format("{0:0}h", hours) : "<1h";
        }

        private static string Age(double years)
        {
            var whole = Math.Floor(Math.Max(0d, years));
            return whole.ToString("0") + (whole == 1d ? " year old" : " years old");
        }

        private static string Signed(double amount)
        {
            return (amount >= 0d ? "+" : string.Empty) + amount.ToString("0");
        }

        private static string KerbalTooltip(RelationsScenario scenario, KerbalState k, ProtoCrewMember pcm)
        {
            if (k == null || pcm == null)
                return null;
            var config = scenario.Config;
            var data = scenario.Data;
            var now = RelationsScenario.Now;
            var sb = new StringBuilder();
            sb.Append("<b>").Append(k.Name).Append("</b>\n");

            if (k.Stage == LifeStage.Egg)
            {
                sb.Append("Egg, hatches in ").Append(Duration(config, k.StageStartUT + config.EggDuration - now)).Append('\n');
            }
            else if (k.Stage == LifeStage.Child)
            {
                sb.Append("Child, ").Append(Age(scenario.Sim.AgeYears(k, now)));
                if (!k.Visitor)
                    sb.Append(", grown up in ").Append(Duration(config, k.StageStartUT + config.ChildDuration - now));
                sb.Append('\n');
            }
            else
            {
                sb.Append(pcm.experienceTrait != null ? pcm.experienceTrait.Title : pcm.trait);
                sb.Append(", level ").Append(pcm.experienceLevel);
                var age = scenario.Sim.AgeYears(k, now);
                if (age >= 0d)
                    sb.Append(", ").Append(Age(age));
                sb.Append('\n');
            }
            var where = Whereabouts(scenario, k);
            if (where != null)
                sb.Append(where).Append('\n');
            if (k.Visitor)
                sb.Append("On a school trip\n");
            else if (k.Stage != LifeStage.Egg && !string.IsNullOrEmpty(k.BirthPlace))
                sb.Append("Born ").Append(k.BirthPlace).Append('\n');
            if (k.ParentA != null || k.ParentB != null)
            {
                sb.Append("Parents: ").Append(RelationsSim.ShortName(k.ParentA));
                if (k.ParentA != null && k.ParentB != null)
                    sb.Append(" and ");
                sb.Append(RelationsSim.ShortName(k.ParentB)).Append('\n');
            }
            if (k.Stage == LifeStage.Egg)
                return sb.ToString().TrimEnd();

            sb.Append("\n<b>Happiness ").Append(k.Happiness.ToString("0")).Append("%</b>\n");
            sb.Append("  ").Append(k.Social.ToString("0")).Append("%  ");
            if (k.AtHome)
            {
                sb.Append("at the KSC");
            }
            else if (k.BestCompany != null)
            {
                var best = data.GetRelationship(k.Name, k.BestCompany);
                sb.Append("company (best bond: ").Append(RelationsSim.ShortName(k.BestCompany));
                sb.Append(' ').Append((best != null ? best.Strength : 0d).ToString("0")).Append("%)");
            }
            else if (k.AloneSince >= 0d)
            {
                sb.Append("alone for ").Append(Duration(config, now - k.AloneSince));
            }
            sb.Append('\n');
            for (int i = 0; i < k.Modifiers.Count; ++i)
            {
                var m = k.Modifiers[i];
                var amount = m.EffectiveAmount(now);
                sb.Append("  <color=").Append(amount >= 0d ? "#8fe07a" : "#ff8a7a").Append('>');
                sb.Append(Signed(amount)).Append("%</color>  ").Append(m.Text);
                if (!m.NoFade)
                    sb.Append(" <color=#999999>(").Append(Duration(config, m.ExpiresUT - now)).Append(")</color>");
                sb.Append('\n');
            }

            if (k.Stage == LifeStage.Adult)
            {
                var effects = ConfigLoader.Effects;
                sb.Append("\nRun x").Append(effects.GetMultiplier(EffectRegistry.RunSpeed, k.Happiness).ToString("0.00"));
                sb.Append("   Research x").Append(effects.GetMultiplier(EffectRegistry.Research, k.Happiness).ToString("0.00"));
                sb.Append("   XP x").Append(effects.GetMultiplier(EffectRegistry.Experience, k.Happiness).ToString("0.00")).Append('\n');
            }

            var rels = data.RelationshipsOf(k.Name);
            rels.Sort((x, y) => y.Strength.CompareTo(x.Strength));
            if (rels.Count > 0)
            {
                sb.Append("\n<b>Bonds</b>\n");
                for (int i = 0; i < rels.Count && i < 5; ++i)
                {
                    var rel = rels[i];
                    sb.Append("  ").Append(RelationsSim.ShortName(rel.Other(k.Name))).Append(' ').Append(rel.Strength.ToString("0")).Append('%');
                    if (rel.IsCouple)
                    {
                        sb.Append("  <color=#ff73b3>partner</color>");
                        var egg = EggLine(scenario, rel);
                        if (egg != null)
                            sb.Append("\n    <color=#999999>").Append(egg).Append("</color>");
                    }
                    else if (rel.Family)
                        sb.Append("  <color=#73b3ff>family</color>");
                    sb.Append('\n');
                }
                if (rels.Count > 5)
                    sb.Append("  and ").Append(rels.Count - 5).Append(" more\n");
            }

            if (k.Ribbons.Count > 0)
            {
                sb.Append("\n<b>Ribbons</b> ").Append(k.Ribbons.Count).Append(", latest: ");
                for (int i = k.Ribbons.Count - 1; i >= 0 && i >= k.Ribbons.Count - 3; --i)
                {
                    if (i < k.Ribbons.Count - 1)
                        sb.Append(", ");
                    sb.Append(scenario.Sim.Defs.ResolveRibbon(k.Ribbons[i].Id).Name);
                }
                sb.Append("\n  <color=#999999>click to see them all</color>\n");
            }

            if (k.Log.Count > 0)
            {
                sb.Append("\n<b>Recently</b>\n");
                for (int i = k.Log.Count - 1; i >= 0 && i >= k.Log.Count - 4; --i)
                {
                    var entry = k.Log[i];
                    sb.Append("  ").Append(entry.Text).Append(" <color=#999999>(").Append(Duration(config, now - entry.UT)).Append(" ago)</color>\n");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string EdgeTooltip(RelationsScenario scenario, Relationship rel)
        {
            var config = scenario.Config;
            var now = RelationsScenario.Now;
            var sb = new StringBuilder();
            sb.Append("<b>").Append(RelationsSim.ShortName(rel.A)).Append(" and ").Append(RelationsSim.ShortName(rel.B)).Append("</b>\n");
            sb.Append("Bond ").Append(rel.Strength.ToString("0")).Append('%');
            if (rel.IsCouple)
                sb.Append("  <color=#ff73b3>couple for ").Append(Duration(config, now - rel.CoupleSince)).Append("</color>");
            else if (rel.Family)
                sb.Append("  <color=#73b3ff>family</color>");
            sb.Append("\nKnown each other for ").Append(Duration(config, now - rel.KnownSince));
            if (now - rel.LastTogetherUT > config.SecondsPerDay)
                sb.Append("\nApart for ").Append(Duration(config, now - rel.LastTogetherUT));
            var closeness = ClosenessLine(scenario, rel);
            if (closeness != null)
                sb.Append('\n').Append(closeness);
            var egg = EggLine(scenario, rel);
            if (egg != null)
                sb.Append('\n').Append(egg);
            return sb.ToString();
        }

        /// <summary>How far a bond can grow between two vessels, or null when the two share one or are apart.</summary>
        private static string ClosenessLine(RelationsScenario scenario, Relationship rel)
        {
            var a = scenario.Data.Get(rel.A);
            var b = scenario.Data.Get(rel.B);
            var groups = scenario.Groups;
            if (a == null || b == null || a.GroupIndex < 0 || a.GroupIndex != b.GroupIndex || a.GroupIndex >= groups.Count
                || a.AtHome || a.Place == b.Place)
                return null;
            var group = groups[a.GroupIndex];
            var distance = group.Distance(a.Place, b.Place);
            var apart = double.IsInfinity(distance) ? string.Empty
                : distance < 1000d ? ", " + distance.ToString("0") + " m apart"
                : ", " + (distance / 1000d).ToString("0.0") + " km apart";
            var cap = scenario.Sim.BondCap(group, a.Place, b.Place);
            return string.Format(rel.Strength >= cap
                ? "In different vessels{0}: the bond does not grow here (limit {1:0}%)"
                : "In different vessels{0}: the bond can grow to {1:0}% here", apart, cap);
        }

        /// <summary>Where a couple stands on having an egg, or null when there is nothing to say.</summary>
        private static string EggLine(RelationsScenario scenario, Relationship rel)
        {
            var config = scenario.Config;
            var now = RelationsScenario.Now;
            switch (scenario.Sim.GetEggStatus(rel, now, scenario.Groups))
            {
                case EggStatus.Disabled:
                    return "No egg: families are switched off in the difficulty settings";
                case EggStatus.BondTooLow:
                    return "No egg yet: needs a bond of " + config.EggThreshold.ToString("0") + "%";
                case EggStatus.Cooldown:
                    return "No egg for another " + Duration(config, rel.LastEggUT + config.Years(config.EggCooldownYears) - now);
                case EggStatus.Apart:
                    return "No egg while they are apart";
                case EggStatus.AtHome:
                    return "No egg at the KSC";
                case EggStatus.NoNest:
                    return HarmonyBootstrap.GrowthModulePatched
                        ? "No egg: needs a free seat in a part with Kolony Growth switched on"
                        : "No egg: needs a free seat";
                case EggStatus.Trying:
                    return "Trying for an egg";
                case EggStatus.Pending:
                    return "An egg is on its way";
            }
            return null;
        }

        /// <summary>Test controls, shown when Debug = true in KR_SETTINGS.</summary>
        private void DrawDebug(RelationsScenario scenario)
        {
            var data = scenario.Data;
            var config = scenario.Config;
            var now = RelationsScenario.Now;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Debug", GUILayout.Width(44f));
            if (GUILayout.Button("+10 days"))
                data.LastUT -= config.Days(10d);
            if (GUILayout.Button("+1 year"))
                data.LastUT -= config.Years(1d);
            if (GUILayout.Button("Mission"))
                ScreenMessages.PostScreenMessage(MissionDirector.OfferNow(scenario), 6f, ScreenMessageStyle.UPPER_CENTER);

            var selected = data.Get(_selected);
            if (selected == null)
            {
                GUILayout.Label("Click a Kerbal for more.");
            }
            else
            {
                if (GUILayout.Button("Mood +20"))
                    scenario.Sim.AddModifier(selected, "Debug", "Debug bonus", 20d, config.Days(20d), now, false);
                if (GUILayout.Button("Mood -20"))
                    scenario.Sim.AddModifier(selected, "Debug", "Debug penalty", -20d, config.Days(20d), now, false);
                if (GUILayout.Button("Bonds +25"))
                {
                    for (int i = 0; i < _nodes.Count; ++i)
                    {
                        var other = data.Get(_nodes[i]);
                        if (other == null || other == selected || other.GroupIndex != selected.GroupIndex || other.Stage == LifeStage.Egg)
                            continue;
                        var rel = data.GetOrCreateRelationship(selected.Name, other.Name, now);
                        rel.Strength = Math.Min(100d, rel.Strength + 25d);
                    }
                }
                if (selected.Partner != null && GUILayout.Button("Egg"))
                {
                    // Skips the bond threshold, the cooldown and the wait, but not the need for a nest.
                    var rel = data.GetRelationship(selected.Name, selected.Partner);
                    var status = scenario.Sim.GetEggStatus(rel, now, scenario.Groups);
                    if (status == EggStatus.BondTooLow || status == EggStatus.Cooldown || status == EggStatus.Trying)
                        rel.EggPendingSince = now;
                    var line = rel != null ? EggLine(scenario, rel) : null;
                    if (line != null)
                        ScreenMessages.PostScreenMessage(line, 6f, ScreenMessageStyle.UPPER_CENTER);
                }
                if (selected.Stage != LifeStage.Adult && GUILayout.Button("Next stage"))
                    selected.StageStartUT = now - (selected.Stage == LifeStage.Egg ? config.EggDuration : config.ChildDuration);
                if (HighLogic.LoadedSceneIsFlight && GUILayout.Button("Dump model"))
                {
                    var pcm = HighLogic.CurrentGame.CrewRoster[selected.Name];
                    if (pcm != null && pcm.KerbalRef != null)
                        StageVisual.DumpHierarchy(pcm.KerbalRef);
                    else if (pcm == null || !DumpOutside(pcm))
                        Log.Info(selected.Name + " has no model right now");
                }
            }
            GUILayout.EndHorizontal();

            // Live tuning of how children look; the values are not saved, copy them into a config.
            if (HighLogic.LoadedSceneIsFlight)
            {
                GUILayout.BeginHorizontal();
                ConfigLoader.ChildScale = DebugSlider("Child size", ConfigLoader.ChildScale, 0.4f, 1f);
                ConfigLoader.ChildHeadScale = DebugSlider("Head", ConfigLoader.ChildHeadScale, 0.5f, 1.6f);
                ConfigLoader.ChildEyeScale = DebugSlider("Eyes", ConfigLoader.ChildEyeScale, 0.8f, 2.2f);
                GUILayout.EndHorizontal();

                // Recolouring a head texture takes a moment, so these only take effect when let go.
                if (_skinSliders == null)
                    _skinSliders = new[] { ConfigLoader.ChildSkinHue, ConfigLoader.ChildSkinHueFemale, ConfigLoader.ChildSkinSaturationFemale };
                GUILayout.BeginHorizontal();
                _skinSliders[0] = Snap(_skinSliders[0], DebugSlider("Boy hue", _skinSliders[0], -1f, 359f), 1f);
                _skinSliders[1] = Snap(_skinSliders[1], DebugSlider("Girl hue", _skinSliders[1], -1f, 359f), 1f);
                _skinSliders[2] = Snap(_skinSliders[2], DebugSlider("Girl colour", _skinSliders[2], 0.2f, 1.2f), 0.05f);
                GUILayout.EndHorizontal();
                if (!Input.GetMouseButton(0) && (_skinSliders[0] != ConfigLoader.ChildSkinHue
                    || _skinSliders[1] != ConfigLoader.ChildSkinHueFemale || _skinSliders[2] != ConfigLoader.ChildSkinSaturationFemale))
                {
                    ConfigLoader.ChildSkinHue = _skinSliders[0];
                    ConfigLoader.ChildSkinHueFemale = _skinSliders[1];
                    ConfigLoader.ChildSkinSaturationFemale = _skinSliders[2];
                    SkinTexture.Clear();
                }
            }
        }

        /// <summary>A slider value rounded to whole steps, but only once the slider has been moved.</summary>
        private static float Snap(float before, float after, float step)
        {
            return after == before ? before : Mathf.Round(after / step) * step;
        }

        /// <summary>Writes the model of a Kerbal who is outside a vessel to the log.</summary>
        private static bool DumpOutside(ProtoCrewMember pcm)
        {
            var vessels = FlightGlobals.VesselsLoaded;
            for (int i = 0; i < vessels.Count; ++i)
            {
                var vessel = vessels[i];
                if (vessel == null || !vessel.isEVA || vessel.rootPart == null || !vessel.rootPart.protoModuleCrew.Contains(pcm))
                    continue;
                StageVisual.DumpHierarchy(vessel.rootPart.transform, "Model of " + pcm.name + " outside");
                return true;
            }
            return false;
        }

        private static float DebugSlider(string label, float value, float min, float max)
        {
            GUILayout.Label(label + " " + value.ToString("0.00"), GUILayout.Width(96f));
            return GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(110f));
        }
    }
}
