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
    
    private static bool IsAPaneOpenThisFrame = false;
    private static bool IsTabSwitching = false;
    private static bool IsInventoryOpen 
        {
            get { return IsAPaneOpenThisFrame || IsTabSwitching; } 
        }

    private static UnityEngine.Camera HudCamera = null;
    private static PlayMakerFSM TabbingControlFSM = null; // Inventory - Inventory Control
    // public static PlayMakerFSM InvFsm = null; // Inv - Inventory Proxy
    // private static InventoryItemCollectableManager InvItemMgr;
    
    private static BoxCollider2D LeftArrowCollider = null;
    private static BoxCollider2D RightArrowCollider = null;

    private void Awake() // Mod startup
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
		
		harmony.PatchAll();
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
                
                LeftArrowCollider = arrowsTfm.Find("Arrow Left").GetComponent<BoxCollider2D>();
                RightArrowCollider = arrowsTfm.Find("Arrow Right").GetComponent<BoxCollider2D>();
            } catch (Exception e) {
                Logger.LogError("Getting Arrow Colliders Failed");
                Logger.LogError(e.Message);
            }
            
            /*
            try {
                UnityEngine.Transform invTfm = inventoryTfm.Find("Inv");
                InvItemMgr = invTfm.GetComponent<InventoryItemCollectableManager>();
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
            if(IsInventoryOpen)
            {
                value = true; // override mouse cursor - set it visible
                IsAPaneOpenThisFrame = false; // reset for next frame (for if it closes)
                // Logger.LogDebug("Overridden");
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
            if(IsInventoryOpen) __instance.inputActions.Attack.Enabled = false;
            else                __instance.inputActions.Attack.Enabled = true;
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

                IsTabSwitching = false; // if in pane, any tab switching is finished.
                IsAPaneOpenThisFrame = true;
                bool mouseOveredElement = false;

                PlayMakerFSM panesCursorControlFsm = GetCursorControlFSM(__instance); // note: if retrieval is expensive (haven't checked), it's possible to do at setup instead, and map with ___paneControl 
                // if this isn't found, we're already doomed.

                var mousepos = HudCamera.ScreenToWorldPoint(Input.mousePosition);

                // TODO: Bug - if arrows don't exist yet (0.0001% of the game), still selectable
                if(LeftArrowCollider!= null && LeftArrowCollider.OverlapPoint(mousepos))
                {
		            // Logger.LogDebug("Mouseover"); // Mouseover works! (finally)
                    
                    // Appearance works perfectly!! (function doesn't :/)
                    // Inv only!
                    // if(InvItemMgr != null) InvItemMgr.SetSelected(LeftArrowCollider.gameObject);

                    panesCursorControlFsm.SetState("L Arrow");
                    // TODO: cursor appearance doesn't always update.
                    mouseOveredElement = true;
                }

                if(RightArrowCollider!= null && RightArrowCollider.OverlapPoint(mousepos))
                {
                    panesCursorControlFsm.SetState("R Arrow");
                    mouseOveredElement = true;
                }
                

                bool mouseButtonJustPressed = Input.GetMouseButtonDown(0);
                if(mouseButtonJustPressed && mouseOveredElement)
                {
                    // Logger.LogDebug("Mouse button just pressed");
                    panesCursorControlFsm.SendEvent("UI CONFIRM");
                    IsTabSwitching = true;
                }

            } catch (Exception e) {
                Logger.LogError("Error: " + e.Message);
            }
        }
    }


}