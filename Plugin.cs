using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
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
        return TabbingControlFSM.ActiveStateName != "Closed"; 
    }

    // Camera
    private static UnityEngine.Camera HudCamera = null;
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
            ClickFunction();
        }

        public void OnMouseOver() // every frame
        {
            // every frame
            if(!IsInventoryOpen()) return;
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
            // Logger.LogDebug("Targeting wrong method");
            CurrentPanesCursorControlFsm.SetState("Accept Input"); // reset state if was at arrow
        }

        protected virtual void WhileMouseOvered()
        {
            // Logger.LogDebug("do this every frame");
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
            var ownSelectable = GetOwnSelectable();

            // Logger.LogInfo("Mouseover inv item");
            if(ownSelectable == null)
            {
                Logger.LogError("selectable is null for " + this.gameObject.name);
                return;

            }

            // again, only visible :/ (not description update)
            InventoryItemManager InvItemMgr = CurrentPaneObject.GetComponent<InventoryItemManager>();
            // InvItemMgr.SetSelected(this.gameObject);
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

            private Vector2 GetLocalMousePos()
            {
                return HudCamera.ScreenToWorldPoint(Input.mousePosition);
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

    public class TestClicker : OnClickClass
    {
        protected override void ClickFunction()
        {
            Logger.LogInfo("Click Test");
        }
        
        protected override void MouseOverFunction()
        {
            Logger.LogInfo("Mouseover Test");
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
    public class testing
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

                
                if(CurrentPaneType == InventoryPaneList.PaneTypes.Map)
                {
                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_OVERMAP)
                    {
                        if(Input.GetAxisRaw("Mouse ScrollWheel") > 0) // scroll up
                        {
                            CurrentPanesCursorControlFsm.SendEvent("UI CONFIRM"); // "zoom in"
                        }
                    } 
                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_ZOOMED) 
                    {
                        if(Input.GetAxisRaw("Mouse ScrollWheel") < 0) // scroll down
                        {
                            MapBack();
                        }
                    }

                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_ZOOMED)
                    {
                        MapDragging.Update();


                        // we want dragging
                    }

                    if(MapZoomStateFsm.ActiveStateName == MAP_STATE_MARKERING)
                    {
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


}