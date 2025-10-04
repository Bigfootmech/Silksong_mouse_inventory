using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using UnityEngine;

namespace inventory_mouse_use;

[BepInPlugin(modGUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
	private const string modGUID = "com.bigfootmech.silksong.inventory_mouse";
    internal static new ManualLogSource Logger; // propagate to other static methods here
	private readonly Harmony harmony = new Harmony(modGUID);
    
    private static bool SwitchedPanesThisFrame = false;
    private static bool IsInventoryOpen() 
    {
        if(TabbingControlFSM == null) return false;
        return TabbingControlFSM.ActiveStateName != "Closed"; 
    }

    private static UnityEngine.Camera HudCamera = null;
    private static PlayMakerFSM TabbingControlFSM = null; // Inventory - Inventory Control
    private static PlayMakerFSM CurrentPanesCursorControlFsm = null; // {X_pane} - Inventory Proxy
    private static GameObject CurrentPaneObject = null;
    // public static PlayMakerFSM InvFsm = null; // Inv - Inventory Proxy
    private static InventoryPaneList PaneList = null;
    // private static InventoryItemCollectableManager InvItemMgr;
    

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
            // do nothing
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
    

    private static PlayMakerFSM GetCursorControlFSM(InventoryPaneInput ipi)
    {
        return GetCursorControlFSM(ipi.transform);
    }
    private static PlayMakerFSM GetCursorControlFSM(UnityEngine.GameObject obj)
    {
        return GetCursorControlFSM(obj.transform);
    }

    // Inv - Inventory Proxy
    // Journal - Inventory Proxy
    // Map - Inventory Proxy
    // Tools - Inventory Proxy
    // Quests - Inventory Proxy
    private static PlayMakerFSM GetCursorControlFSM(UnityEngine.Transform tfm)
    {
        PlayMakerFSM[] listFsms = tfm.GetComponents<PlayMakerFSM>();

        foreach(PlayMakerFSM fsm in listFsms)
        {
            // Logger.LogDebug(fsm.FsmName);
            // if(fsm.FsmName.Contains("Proxy"))
            if(fsm.FsmName == "Inventory Proxy")
            {
                // Logger.LogDebug("Got Proxy");
                return fsm;
            }
        }

        return null;
    }

    /*
    private static bool IsMouseover(BoxCollider2D coll)
    {
        if(coll == null || HudCamera == null) return false;

        return coll.OverlapPoint(HudCamera.ScreenToWorldPoint(Input.mousePosition));
    }*/

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


            /*
            try {
                UnityEngine.Transform invTfm = inventoryTfm.Find("Inv");
            } catch (Exception e) {
                Logger.LogError("Getting InvItemMgr Failed");
                Logger.LogError(e.Message);
            }
            */

            
            /*
            
            UnityEngine.Transform needleTfm = invTfm.Find("Inv_Items").Find("Needle");
            
            InventoryItemNail nailScript = needleTfm.GetComponent<InventoryItemNail>();
            
             */
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

                // Which pane? (get FSM)
                PlayMakerFSM currentPaneFsm = GetCursorControlFSM(__instance); // note: if retrieval is expensive (haven't checked), it's possible to do at setup instead, and map with ___paneControl 
                CurrentPaneObject = __instance.gameObject;
                // if this isn't found, we're already doomed.
                
                SwitchedPanesThisFrame = false;
                if(CurrentPanesCursorControlFsm != currentPaneFsm)
                {
                    SwitchedPanesThisFrame = true; // duration = ~1 frame
                    CurrentPanesCursorControlFsm = currentPaneFsm;
                }
                


            } catch (Exception e) {
                Logger.LogError("Error: " + e.Message);
            }
        }
    }


}