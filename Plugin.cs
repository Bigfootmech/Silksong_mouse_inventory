using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static inventory_mouse_use.Plugin.OnClickInventoryItem;

namespace inventory_mouse_use;

[BepInPlugin(modGUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
	private const string modGUID = "com.bigfootmech.silksong.inventory_mouse";
    internal static new ManualLogSource Logger; // propagate to other static methods here
	private readonly Harmony harmony = new Harmony(modGUID);
    
    private const string CURSOR_CONTROL_FSM_NAME = "Inventory Proxy";
    private const string MAP_STATE_OVERMAP = "Wide Map";
    private const string MAP_STATE_ZOOMED = "Zoomed In";
    private const string MAP_STATE_MARKERING = "Marker Select Menu";
    
    private static bool SwitchedPanesThisFrame = false;
    private static bool IsInventoryOpen() 
    {
        if(TabbingControlFSM == null) return false;

        var state = TabbingControlFSM.ActiveStateName;
        
        if(state == "Closed" || state == "Init") return false;

        return true; 
    }
    private static bool Scrolling = false;
    private const int FRAMES_TO_BLOCK_MOUSEOVER_WHILE_SCROLLING = 100; // magic number
    private static int ScrollSmoothCountdown = 0;
    private static float MAGIC_GAME_PIN_COLLIDER_DISTANCE = 0.57f;
    private static float PIN_MAP_PAN_SPEED = 3f;

    // private static int ScrollSmoothCountUp = 0;
    private static bool IsScrolling()
    {
        return Scrolling || Input.GetAxisRaw("Mouse ScrollWheel") != 0;
    }
    private static void SetScrolling(bool setVal, bool instant = false)
    {
        if(setVal) // set scroll on
        {
            Scrolling = true;
            // ScrollSmoothCountUp = 0;
            ScrollSmoothCountdown = FRAMES_TO_BLOCK_MOUSEOVER_WHILE_SCROLLING;
            return;
        }

        // countdown adjust
        if(instant)
        {
            ScrollSmoothCountdown = 0;
        } else {
            if(ScrollSmoothCountdown > 0)
            {
                ScrollSmoothCountdown--;
            } 
        }
        
        // if end, set scroll off
        if(ScrollSmoothCountdown <= 0) 
        {
            // ScrollSmoothCountUp = 0;
            Scrolling = false;
        } 
    }


    // Camera
    private static UnityEngine.Camera HudCamera = null;
    public static Vector2 GetLocalMousePos()
    {
        return HudCamera.ScreenToWorldPoint(Input.mousePosition);
    }

    // Inventory Overall
    private static PlayMakerFSM TabbingControlFSM = null; // Inventory - Inventory Control
    private static InventoryPaneList PaneList = null;
    // Tab/Pane
    private static InventoryPaneList.PaneTypes CurrentPaneType = InventoryPaneList.PaneTypes.None;
    private static GameObject CurrentPaneObject = null;
    // Inv - Inventory Proxy
    // Journal - Inventory Proxy
    // Map - Inventory Proxy
    // Tools - Inventory Proxy
    // Quests - Inventory Proxy
    private static PlayMakerFSM CurrentPanesCursorControlFsm = null; // {X_pane} - Inventory Proxy
    // public static PlayMakerFSM InvFsm = null; // Inv - Inventory Proxy
    // private static InventoryItemCollectableManager InvItemMgr;
    private static PlayMakerFSM MapZoomStateFsm = null;
    private static GameMap ZoomedMapControl = null;
    private static DraggingAction MapDragging = new();
    
    private static Dictionary<InventoryPaneList.PaneTypes, ScrollHover> ScrollHoverSet = new();
    private static ScrollHover GetScrollHoverForPane(InventoryPaneList.PaneTypes currentPaneType)
    {
        return ScrollHoverSet.GetValueSafe(currentPaneType);
    }
    
    private void Awake() // Mod startup
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
		
		harmony.PatchAll();
    }

    public class OnClickClass : MonoBehaviour
    {
        private bool MouseAlreadyIn = false;
        
        public void OnMouseUp()
        {
            // Logger.LogDebug("MouseUp"); // for implementing alternate mouseclick modes
            CurrentPanesCursorControlFsm.SendEvent("UI CONFIRM RELEASED"); // temp fix?
        }

        public void OnMouseDown()
        {
            if(!IsInventoryOpen()) return;
            // Logger.LogDebug("I was clicked");
            if(IsScrolling()) return; // disable clicking while wrong element set
            ClickFunction();
        }

        public void OnMouseOver() // every frame
        {
            // every frame
            if(!IsInventoryOpen()) return; // if not in inventory, ignore button contact
            
            if(IsScrolling()) return; // disable mouse-over while scrolling
            WhileMouseOvered();
            if(MouseAlreadyIn && !SwitchedPanesThisFrame) return;
            // only once, OR if just switched frames
            // Issue: Cursor ZIPS to mouse-over after switching -- any way to just start on element?
            MouseAlreadyIn = true; // only once
            MouseOverFunction();
            // Logger.LogDebug("Mouseover");
        }

        public void OnMouseExit() // once
        {
            MouseAlreadyIn = false;
            // Logger.LogDebug("Mouse left");
        }

        protected virtual void ClickFunction()
        {
            // Logger.LogDebug("Inner");
            CurrentPanesCursorControlFsm.SendEvent("UI CONFIRM");
        }

        protected virtual void MouseOverFunction()
        {
            // Logger.LogInfo("reset state? on " + SafeToString(gameObject.name));
            CurrentPanesCursorControlFsm.SetState("Accept Input"); // reset state if was at arrow
        }

        protected virtual void WhileMouseOvered()
        {
            // Logger.LogInfo("do this every frame, EVERY item");
        }
    }

    public class OnClickBorderArrow : OnClickClass
    {
        public string stateName;

        protected override void ClickFunction()
        {
            // Logger.LogDebug("Outer");

            base.ClickFunction();
        }
        
        protected override void MouseOverFunction()
        {
            // Logger.LogInfo("reset state? on " + SafeToString(gameObject.name));
            // Logger.LogDebug("Targeting correct method");
            CurrentPanesCursorControlFsm.SetState(stateName);
        }
    }

    public class OnClickTab : OnClickClass
    {
        protected override void ClickFunction()
        {
            // Logger.LogInfo("Go to Pane " + SafeToString(targetPane));
            SwitchInventoryTabTo(GetTargetPane());
        }
        
        protected override void MouseOverFunction()
        {
            // Logger.LogInfo("mouseover on " + SafeToString(gameObject.name));
            InventoryItemManager InvItemMgr = CurrentPaneObject.GetComponent<InventoryItemManager>();
            InvItemMgr.SetSelected(this.gameObject);
        }

        private InventoryPaneList.PaneTypes GetTargetPane() // not available at inventory start
        {
            var paneScript = this.gameObject.GetComponent<InventoryPaneListItem>();
            return GetPaneType(paneScript.currentPane);
        }

        private InventoryPaneList.PaneTypes GetPaneType(InventoryPane pane)
        {
            return (InventoryPaneList.PaneTypes) PaneList.GetPaneIndex(pane);
        }

        private InventoryPaneList.PaneTypes GetPaneType(string paneName)
        {
            return (InventoryPaneList.PaneTypes) PaneList.GetPaneIndex(paneName);
        }
    }

    public class OnClickInventoryItem : OnClickClass
    {
        // InventoryItemSelectable OwnSelectable;

        
        protected override void MouseOverFunction()
        {
            // can fail to find current pane early
            // we could do a whole setup, categorizing each thing to their panes, 
            // and getting the right ones...
            // Or we could just wait until the pane is grabbed
            if(CurrentPaneObject == null) return;
            // so this is more-or-less just an error-squelch.

            var ownSelectable = GetOwnSelectable();

            // Logger.LogInfo("Mouseover inv item");
            if(ownSelectable == null)
            {
                Logger.LogError("selectable is null for " + this.gameObject.name);
                return;
            }

            InventoryItemManager InvItemMgr = CurrentPaneObject.GetComponent<InventoryItemManager>();
            InvItemMgr.SetSelected(ownSelectable,null);

            base.MouseOverFunction();
        }

        public class DraggingAction
        {
            public bool IsDragging;
            private Vector2 MouseLastPos;

            public void Update()
            {
                // Logger.LogInfo("Is being processed");

                if(IsDragging)
                {
                    DoDragAction();
                }
                
                if (Input.GetMouseButtonDown(0)) // leftclick just pressed
                {
                    // Logger.LogInfo("MouseDown");
                    if(!IsDragging)
                    {
                        // StartDragTimer();
                        StartDragging();
                    } 
                } 
                
                if (Input.GetMouseButtonUp(0)){ // left released
                    if(IsDragging) // and WAS dragging
                    {
                        Release();
                    }
                }

            }

            private void StartDragging()
            {
                IsDragging = true;
                MouseLastPos = GetLocalMousePos();
            }

            public void Stop()
            {
                Release();
            }

            private void Release()
            {
                IsDragging = false;
            }

            private void DoDragAction()
            {
                if(!IsInventoryOpen()) 
                {
                    Release();
                    return;
                }

                SetMapCoordsByMouse();
            }

            private Vector2 GetMouseDragDelta()
            {
                Vector2 currMousePos = GetLocalMousePos();
                Vector2 mouseDelta = currMousePos - MouseLastPos;
                MouseLastPos = currMousePos;
                return mouseDelta;
            }

            private void SetMapCoordsByMouse()
            {
                if(CurrentPaneType != InventoryPaneList.PaneTypes.Map ||
                    MapZoomStateFsm.ActiveStateName != MAP_STATE_ZOOMED)
                {
                    Release();
                    return;
                }
                
                Vector2 mouseDelta = GetMouseDragDelta();
                Vector2 mapCurrPos = ZoomedMapControl.transform.localPosition;

                ZoomedMapControl.UpdateMapPosition(mapCurrPos + mouseDelta);
            }
        }

        private InventoryItemSelectable GetOwnSelectable()
        {
            return this.gameObject.GetComponent<InventoryItemSelectable>();
        }
    }

    public class ScrollArrowHover : OnClickClass
    {
        internal enum Dir {Up, Down };

        private const float SPEED = 0.2f; // MAGIC NUMBER (hover scroll speed)

        public ScrollView ScrollControllerScript;
        private Vector3 TravelVector = new(0,SPEED,0);

        protected override void WhileMouseOvered()
        {
            // var delta = Time.deltaTime; // always 0, not in use?
            ScrollControllerScript.transform.localPosition += TravelVector;
        }

        internal void SetDirection(Dir direction)
        {
            if(direction == Dir.Down)
                TravelVector.y = SPEED;
            else
                TravelVector.y = -SPEED;
        }
    }

    public class TestClicker : OnClickClass
    {
        protected override void ClickFunction()
        {
            Logger.LogInfo("Click Test. " + SafeToString(gameObject.name));
        }
        
        protected override void MouseOverFunction()
        {
            Logger.LogInfo("Mouseover Test." + SafeToString(gameObject.name));
        }

        protected override void WhileMouseOvered()
        {
            Logger.LogInfo("do this every frame." + SafeToString(gameObject.name));
        }
    }

    public class ScrollHover : MonoBehaviour
    {
        private const float SCROLL_WHEEL_SPEED = 8.0f; // MAGIC NUMBER (wheel scroll speed)

        // public Bounds Bounds;
        public InventoryPaneList.PaneTypes MyPane;
        public ScrollView ScrollControllerScript;

        public bool CheckMouseOver()
        {
            Vector2 screenLoc = transform.position;
            var mouseOffset = GetLocalMousePos() - screenLoc;

            return ScrollControllerScript.contentBounds.Contains(mouseOffset);
        }

        public void Update()
        {
            // Logger.LogInfo("Update triggered: " + SafeToString(gameObject.name));
            if(!IsInventoryOpen() || CurrentPaneType != MyPane) return;
            // Logger.LogInfo("Correct Pane");
            if(!CheckMouseOver()) 
            {
                SetScrolling(false, true);
                return;
            } 
            // inventory open, to the correct pane, and moused-over

            Scroll();
            // Logger.LogInfo("Mousing over a scroll area! " + SafeToString(gameObject.name));
        }

        private void Scroll()
        {
            float mouseWheelSpeed = Input.GetAxisRaw("Mouse ScrollWheel");

            if(mouseWheelSpeed != 0)
            {
                SetScrolling(true);
                float modified = mouseWheelSpeed * SCROLL_WHEEL_SPEED;

                ScrollControllerScript.transform.localPosition -= new Vector3(0,modified,0);
            } else {
                SetScrolling(false);
            }
        }
    }

    private static string SafeToString(System.Object ob) // helper
    {
        return ob?.ToString() ?? "null";
    }

    private static void SwitchInventoryTabTo(InventoryPaneList.PaneTypes pane)
    {
        if(TabbingControlFSM == null) return;
		TabbingControlFSM.FsmVariables.FindFsmInt("Target Pane Index").Value = (int) pane;
		TabbingControlFSM.SendEvent("MOVE PANE TO");
    }
    
    [HarmonyPatch(typeof(InventoryPaneList), "Start")]
    public class AfterInventoryIsCreated_Setup
    {
	    [HarmonyPostfix]
	    static void Postfix(InventoryPaneList __instance
		    // , IEnumerator __result
		    // , ref AsyncOperation ___loadop
		    )
	    {

            if(__instance == null) Logger.LogError("Inventory Setup Hook Failed");
		    // Logger.LogDebug("Inventory Started!");
            
            PaneList = __instance;

            UnityEngine.Transform inventoryTfm = __instance.transform;

            try {
                var camTfm = __instance.transform.parent.parent;
                HudCamera = camTfm.GetComponent<Camera>();
            } catch (Exception e) {
                Logger.LogError("Getting Hud Camera Failed");
                Logger.LogError(e.Message);
            }
            
            try {
                TabbingControlFSM = __instance.transform.GetComponent<PlayMakerFSM>();
                // alternatively, PlayMakerFSM.FindFsmOnGameObject(__instance, "Inventory Control");
            } catch (Exception e) {
                Logger.LogError("Getting Tabbing Control FSM (\"Inventory Control\") Failed");
                Logger.LogError(e.Message);
            }
            
            try {
                UnityEngine.Transform arrowsTfm = inventoryTfm.Find("Border").Find("Arrows");

                OnClickBorderArrow scriptAdded = 
                    arrowsTfm.Find("Arrow Left").gameObject.AddComponent<OnClickBorderArrow>();
                scriptAdded.stateName = "L Arrow";

                
                OnClickBorderArrow scriptAdded2 = 
                    arrowsTfm.Find("Arrow Right").gameObject.AddComponent<OnClickBorderArrow>();
                scriptAdded2.stateName = "R Arrow";
                
            } catch (Exception e) {
                Logger.LogError("Setting up Arrow click scripts Failed");
                Logger.LogError(e.Message);
            }
            
            try {
                UnityEngine.Transform tabIconSet = inventoryTfm.Find("Border").Find("PaneListDisplay").Find("List Items");
                
                foreach(UnityEngine.Transform tabIcon in tabIconSet) // loop CHILDREN (not components)
                {
                    BoxCollider2D addedColl = tabIcon.gameObject.AddComponent<BoxCollider2D>();
                    // Logger.LogInfo("addedColl " + SafeToString(addedColl));
                    OnClickTab addedScript = tabIcon.gameObject.AddComponent<OnClickTab>();
                    // Logger.LogInfo("addedScript " + SafeToString(addedScript));

                }
            } catch (Exception e) {
                Logger.LogError("Getting Tabs Failed");
                Logger.LogError(e.Message);
            }
            
            try {
                UnityEngine.Transform invTfm = inventoryTfm.Find("Inv");
                UnityEngine.Transform currencyAndSpoolGroupTfm = invTfm.Find("Inv_Items").Find("Needle Shift");
                foreach(Transform child in currencyAndSpoolGroupTfm) {
                    if(child.name != "Spool Group")
                        child.gameObject.AddComponent<OnClickInventoryItem>();
                }
            } catch (Exception e) {
                Logger.LogError("Failed attaching onclick to missed inv items");
                Logger.LogError(e.Message);
            }
            
            
            try {
                UnityEngine.Transform mapTfm = inventoryTfm.Find("Map");
                MapZoomStateFsm = mapTfm.gameObject.LocateMyFSM("UI Control");
            } catch (Exception e) {
                Logger.LogError("Failed setting up map zoom control.");
                Logger.LogError(e.Message);
            }

            try {
                UnityEngine.Transform mapTfm = inventoryTfm.Find("Map");
                UnityEngine.Transform overmapHolderTfm = mapTfm.Find("World Map").Find("Map Offset");
                UnityEngine.Transform overmapTfm = overmapHolderTfm.GetChild(0); // we hope.
                foreach(Transform overmapSegments in overmapTfm) {
                    var go = overmapSegments.gameObject;
                    // Logger.LogInfo(go.name);
                    if(overmapSegments.gameObject.name.StartsWith("Wide_map_"))
                    {
                        // Logger.LogInfo("Accepted");
                        BoxCollider2D addedColl = go.AddComponent<BoxCollider2D>();
                        go.AddComponent<OnClickInventoryItem>();
                    } else {
                        // Logger.LogInfo("Rejected");
                    }
                    
                }
            } catch (Exception e) {
                Logger.LogError("Failed attaching onclick to missed overmap items");
                Logger.LogError(e.Message);
            }
	    }
    }
    
    [HarmonyPatch(typeof(ScrollView), "Start")]
    public class GetAllScrollViews
    {
	    [HarmonyPostfix]
	    static void Postfix(ScrollView __instance
		    // , ref AsyncOperation ___loadop
		    )
	    {
            var hoverUp = __instance.upArrow.transform.parent.gameObject.AddComponent<ScrollArrowHover>();
            var hoverDown = __instance.downArrow.transform.parent.gameObject.AddComponent<ScrollArrowHover>();
            
            hoverUp.ScrollControllerScript = __instance;
            hoverDown.ScrollControllerScript = __instance;
            hoverUp.SetDirection(ScrollArrowHover.Dir.Up);
            hoverDown.SetDirection(ScrollArrowHover.Dir.Down);
            
            var hoverComp = __instance.gameObject.AddComponent<ScrollHover>();
            hoverComp.ScrollControllerScript = __instance;

            var paneType = FindPaneTypeForThis(__instance);
            hoverComp.MyPane = paneType;
            RegisterScroller(paneType, hoverComp);

        }

        private static void RegisterScroller(InventoryPaneList.PaneTypes paneType, ScrollHover testH)
        {
            if(paneType == InventoryPaneList.PaneTypes.None) return;
            ScrollHoverSet.Add(paneType, testH);
        }

        private static InventoryPaneList.PaneTypes FindPaneTypeForThis(ScrollView instance)
        {
            var objName = instance.gameObject.name;
            // Logger.LogInfo("obj name " + SafeToString(objName));
            // this can be a map, but honestly, this is more readable imo.
            if(objName == "Equipment") return InventoryPaneList.PaneTypes.Inv;
            if(objName == "Tool List") return InventoryPaneList.PaneTypes.Tools;
            if(objName == "Quest List") return InventoryPaneList.PaneTypes.Quests;
            if(objName == "Enemy List") return InventoryPaneList.PaneTypes.Journal;
            return InventoryPaneList.PaneTypes.None;
        }
    }
            
    [HarmonyPatch(typeof(GameMap), "Start")]
    public class HookRealMapStart
    {
	    [HarmonyPostfix]
	    static void Postfix(GameMap __instance
		    // , ref AsyncOperation ___loadop
		    )
	    {
            ZoomedMapControl = __instance;
        }
    }

    [HarmonyPatch(typeof(InventoryItemSelectable), "add_OnSelected")]
    public class TryOnSelected
    {
        [HarmonyPrefix]
        static void Prefix(InventoryItemSelectable __instance
            , System.Action<InventoryItemSelectable> __0
            )
        {
            // Logger.LogInfo("Does this even run? add_OnSelected");
            // Logger.LogInfo("inst? " + SafeToString(__instance));
            // Logger.LogInfo("__0? " + SafeToString(__0));
            // Logger.LogInfo("type? " + SafeToString(__instance.GetType()));
            // var obj = __instance.gameObject;
            // Logger.LogInfo("obj = " + SafeToString(__instance.gameObject));
            // looking out for Wide_map__xxxx_{name}
            __instance.gameObject.AddComponent<OnClickInventoryItem>();
            // Logger.LogInfo("Added click component?");
        }
    }
        
    [HarmonyPatch(typeof(InputHandler), "SetCursorVisible")]
    public class OverrideCursorVisible
    {
        [HarmonyPrefix]
        static void Prefix(InputHandler __instance
            , ref bool value // hook value arg to pass to real SetCursorVisible function
            )
        {
            // polling
            if(IsInventoryOpen())
            {
                value = true; // override mouse cursor - set it visible
            }
        }
    }

    [HarmonyPatch(typeof(InputHandler), "Update")]
    public class DisableAttackForMouseClickJank
    {
        [HarmonyPrefix]
        static void Prefix(InputHandler __instance)
        {
            // while inventory open, disable attack
            // to make more consistent, could check for attack (or 'bind/focus/spell') binding being mouseclick
            if(IsInventoryOpen()) __instance.inputActions.Attack.Enabled = false;
            else                  __instance.inputActions.Attack.Enabled = true;
        }
    }

    // While a TAB is open. (and not switching)
    [HarmonyPatch(typeof(InventoryPaneInput), "Update")]
    public class OnUpdateDo
    {
        [HarmonyPostfix]
        static void Postfix(InventoryPaneInput __instance
            , ref InventoryPaneList ___paneList
            , ref InventoryPaneList.PaneTypes ___paneControl
            // , ref float ___actionCooldown, ref InputHandler ___ih
            // , ref Platform ___platform, ref bool ___wasExtraPressed
            // , ref bool ___isRepeatingDirection, ref bool ___wasSubmitPressed
            // 
            // , ref bool ___allowRightStickSpeed
            // , ref bool ___isScrollingFast, ref float ___directionRepeatTimer
            // , ref bool ___isInInventory, ref bool ___isRepeatingSubmit
            // , ref InventoryPaneBase.InputEventType ___lastPressedDirection
            )
        {
            // Inventory is open (polling)

            try { // if we error, please tell us why :/
                

                // Logger.LogInfo("Scrolling = " + SafeToString(Scrolling) + ", cd = " + SafeToString(ScrollSmoothCountdown) + ", cu = " + SafeToString(ScrollSmoothCountUp++));

                SwitchedPanesThisFrame = false;
                if(CurrentPaneType != ___paneControl)
                {
                    SwitchedPanesThisFrame = true; // duration = ~1 frame
                    CurrentPaneType = ___paneControl;
                    CurrentPaneObject = __instance.gameObject;
                    // if this isn't found, we're already doomed.
                    CurrentPanesCursorControlFsm = CurrentPaneObject.LocateMyFSM(CURSOR_CONTROL_FSM_NAME); // also possible to load these at start, and retrieve with paneControl
                }
                
                if (Input.GetMouseButtonDown(1)) // right clicked
                {
                    InventoryBack();
                }

                ScrollHover scrollHover = GetScrollHoverForPane(CurrentPaneType);
                if(scrollHover != null) scrollHover.Update();
                
                if(CurrentPaneType == InventoryPaneList.PaneTypes.Map)
                {
                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_OVERMAP)
                    {
                        if(Input.GetAxisRaw("Mouse ScrollWheel") > 0) // scroll up
                        {
                            CurrentPanesCursorControlFsm.SendEvent("UI CONFIRM"); // "zoom in"
                        }

                        MapDragging.Stop();
                    } 
                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_ZOOMED) 
                    {
                        if(Input.GetAxisRaw("Mouse ScrollWheel") < 0) // scroll down
                        {
                            MapBack();
                        }

                        MapDragging.Update();


                        // we want dragging
                    }

                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_MARKERING)
                    {
                        MapDragging.Stop();
                        // we want to replace cursor with selector
                    }
                }

            } catch (Exception e) {
                Logger.LogError("Error: " + e.Message);
            }
        }

        private static void InventoryBack()
        {
            if(CurrentPaneType != InventoryPaneList.PaneTypes.Map)
            {
                TabbingControlFSM.SendEvent("BACK"); // doesn't work zoomed in... but otherwise ok
                return;
            }

            MapBack();
        }

        private static void MapBack()
        {
            var mapZoomStateFsm = CurrentPaneObject.LocateMyFSM("UI Control");

            if(mapZoomStateFsm.ActiveStateName == MAP_STATE_OVERMAP)
            {
                TabbingControlFSM.SendEvent("BACK"); // normal window = just back
                return;
            }
            
            mapZoomStateFsm.SendEvent("UI CANCEL");
        }
    }



    
    [HarmonyPatch(typeof(MapMarkerMenu), "Update")]
    // [HarmonyPatch(typeof(MapMarkerMenu), "PanMap")]
    public class PinsCursorMouseControl
    {
        // [HarmonyPrefix] 
        // static void Prefix(MapMarkerMenu __instance
        [HarmonyPostfix]
        static void Postfix(MapMarkerMenu __instance
		    // , bool __result
            , ref GameObject ___placementCursor
            // , ref bool ___collidingWithMarker
            // , ref float ___placementCursorMinX
            // , ref float ___placementCursorMaxX
            // , ref float ___placementCursorMinY
            // , ref float ___placementCursorMaxY
            , ref List<GameObject> ___collidingMarkers
            , ref GameMap ___gameMap
            , ref GameObject ___gameMapObject
            , ref bool ___inPlacementMode
            , ref float ___placementTimer
            , ref float ___panSpeed
            )
        {
            if(!___inPlacementMode) return;
            // Logger.LogInfo("time = " + SafeToString(Time.unscaledDeltaTime));

            var isPanning = false;
            var mousePosLimited = GetLocalMousePos();
            
            // Logger.LogInfo("mous = " + SafeToString(mousePos));
            // Logger.LogInfo("marker bounds = " + SafeToString(___gameMap.MapMarkerBounds));
            // Logger.LogInfo("in = " + SafeToString(___gameMap.MapMarkerBounds.Contains(mousePos)));
            
            // var bounds = ___gameMap.MapMarkerBounds;
            var bounds = ___gameMap.mapMarkerScrollArea; // "correct"
            
            if(isPanning = IsMousePastEdge(bounds, mousePosLimited)) {

                Vector3 panVec = GetPanVecAndConstrainMouse(ref mousePosLimited, 
                    bounds, ___panSpeed,___placementTimer);

                if(___gameMap.CanMarkerPan()) {
                    ___gameMapObject.transform.localPosition -= panVec;
                    ___gameMap.KeepWithinBounds(InventoryMapManager.SceneMapMarkerZoomScale);
                }

            }



            bool mouseCloseToPin = false;
            if(!isPanning)
            {
                if(IsAPinSelected(___collidingMarkers))
                {
                    Vector2 pinPos = GetPinCorrectedPos(___collidingMarkers, ___gameMap);
                    mouseCloseToPin = AreTwoPointsClose(mousePosLimited, pinPos);
                }
            }
            // Closer Pins = nice, but bugs the collisions (ie: removal) process.
            //      theoretically, still doable, but needs more code :/
            //      ie: re-doing MORE of how the game handles things.
            //
            // AddToCollidingList(GameObject go)
            // RemoveFromCollidingList(GameObject go)
            // SetCollisionTextAndState(__instance, mouseCloseToPin); // set by above
            
            ProcessMouseCursorMovement(// ___collidingMarkers, // ref __result, 
                ref ___placementCursor, // ___gameMap, 
                mouseCloseToPin, mousePosLimited);

            // "Adjust" cursor towrds pin if mouse-over = game does it.
            
            // Process Inputs
            if(!isPanning)
            {
                if (Input.GetMouseButtonDown(0)) // left clicked
                {
                    ProcessMarkerPutRemove(__instance, mouseCloseToPin);
                }
            }

            
            if(Input.GetAxisRaw("Mouse ScrollWheel") < 0) // scroll down
            {
                __instance.MarkerSelectLeft();
            }
            if(Input.GetAxisRaw("Mouse ScrollWheel") > 0) // scroll down
            {
                __instance.MarkerSelectRight();
            }
            


        }

        private static Vector3 GetPanVecAndConstrainMouse(ref Vector2 mousePosLimited, 
            Bounds bounds, float ___panSpeed, float ___placementTimer)
        {
            var boundLower = bounds.center - bounds.extents;
            var boundUpper = bounds.center + bounds.extents;

            float modPanSpd = PIN_MAP_PAN_SPEED * ___panSpeed * Time.unscaledDeltaTime;
            Vector3 panVec = new();
            if(mousePosLimited.x < boundLower.x) {
                mousePosLimited.x = boundLower.x;
                if(___placementTimer <= 0f)
                    panVec.x = -modPanSpd;
            } else if(mousePosLimited.x > boundUpper.x) {
                mousePosLimited.x = boundUpper.x;
                if(___placementTimer <= 0f)
                    panVec.x = modPanSpd;
            }
            if(mousePosLimited.y < boundLower.y) {
                mousePosLimited.y = boundLower.y;
                if(___placementTimer <= 0f)
                    panVec.y = -modPanSpd;
            } else if(mousePosLimited.y > boundUpper.y) {
                mousePosLimited.y = boundUpper.y;
                if(___placementTimer <= 0f)
                    panVec.y = modPanSpd;
            }

            return panVec;
        }

        private static bool IsMousePastEdge(Bounds bounds, Vector2 mousePosLimited)
        {
            return !bounds.Contains(mousePosLimited);
        }

        // private static void SetCollisionTextAndState(MapMarkerMenu instance, 
        //     bool mouseCloseToPin)
        // {
        //     if(mouseCloseToPin)
        //         instance.IsColliding();
        //     else
        //         instance.IsNotColliding();
        // }

        private static void ProcessMouseCursorMovement( 
            // ref bool hasCursorMoved, 
            ref GameObject placementCursor,
            bool mouseoverPin, Vector2 mousePos)
        {
            if(mouseoverPin) return; // if we're mousing a pin, let game handle transition?
            if(!HasMouseMoved()) return; // no mouse move = ignore input.

            MoveCursorToMouse(// ref hasCursorMoved, 
                ref placementCursor, mousePos);
        }

        private static Vector2 GetPinCorrectedPos(List<GameObject> collidingMarkers, 
            GameMap gameMap)
        {
            return collidingMarkers.Last().transform.position -
                gameMap.transform.parent.position;
        }

        private static bool AreTwoPointsClose(Vector2 p1, 
            Vector2 p2)
        {
            var distance = (p1 - p2).magnitude;
            // Logger.LogInfo("distance = " + SafeToString(distance));
            return distance < MAGIC_GAME_PIN_COLLIDER_DISTANCE;
        }

        private static bool IsAPinSelected(List<GameObject> collidingMarkers)
        {
            return collidingMarkers.Count > 0;
        }

        private static void MoveCursorToMouse(// ref bool hasCursorMoved, 
            ref GameObject placementCursor, Vector2 mousePos)
        {
            placementCursor.transform.position = mousePos;
            //hasCursorMoved = true;
        }

        private static void ProcessMarkerPutRemove(MapMarkerMenu instance, bool mouseover)
        {
            if (mouseover)// instance.collidingWithMarker)
			{
				instance.RemoveMarker();
			}
			else
			{
				instance.PlaceMarker();
			}
        }

        private static bool IsMouseWithinBounds(Vector2 mousePos, 
            float placementCursorMinX, float placementCursorMaxX, 
            float placementCursorMinY, float placementCursorMaxY)
        {
            if(mousePos.x < placementCursorMinX) return false;
            if(mousePos.x > placementCursorMaxX) return false;
            if(mousePos.x < placementCursorMinY) return false;
            if(mousePos.x > placementCursorMaxY) return false;
            return true;
        }

        private static bool HasMouseMoved()
        {
            return (Input.GetAxis("Mouse X") != 0) || (Input.GetAxis("Mouse Y") != 0);
        }
    }


}