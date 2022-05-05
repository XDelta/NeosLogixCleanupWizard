using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

using NeosModLoader;

using FrooxEngine;
using FrooxEngine.LogiX;
using FrooxEngine.UIX;
using BaseX;
using CodeX;

namespace NeosLogixCleanupWizard {
	public class NeosLogixCleanupWizard : NeosMod {
		public override string Name => "Logix Cleanup Wizard";
		public override string Author => "Delta";
		public override string Version => "1.0.2";
		public override string Link => "https://github.com/Delta/NeosLogixCleanupWizard";

		public override void OnEngineInit() {
			/*Harmony harmony = new Harmony("tk.deltawolf.LogixCleanupWizard");
			harmony.PatchAll();*/
			Engine.Current.RunPostInit(AddMenuOption);
		} 
		void AddMenuOption() {
			DevCreateNewForm.AddAction("Editor", "LogiX Cleanup Wizard", (x) => LogixCleanupWizard.GetOrCreateWizard(x));
		}

		class LogixCleanupWizard {
			public static LogixCleanupWizard GetOrCreateWizard(Slot x) {
				if (_Wizard != null) {
					WizardSlot.PositionInFrontOfUser(float3.Backward, distance: 1f);
					return _Wizard;
				} else {
					return new LogixCleanupWizard(x);
				}
			}
			static LogixCleanupWizard _Wizard;
			static Slot WizardSlot;

			readonly ReferenceField<Slot> processingRoot;
			readonly ValueField<bool> removeLogixReferences;
			readonly ValueField<bool> removeLogixInterfaceProxies;

			readonly Button cleanUnusedLogixComponents;
			readonly Button destroyInterfaces;
			readonly Button removeEmptyRefs;
			readonly Button removeEmptyCasts;

			readonly Text statusText;
			void UpdateStatusText(string info) {
				statusText.Content.Value = info;
			}

			LogixCleanupWizard(Slot x) {
				_Wizard = this;

				WizardSlot = x;
				WizardSlot.Tag = "Developer";
				WizardSlot.OnPrepareDestroy += Slot_OnPrepareDestroy;
				WizardSlot.PersistentSelf = false;

				NeosCanvasPanel canvasPanel = WizardSlot.AttachComponent<NeosCanvasPanel>();
				canvasPanel.Panel.AddCloseButton();
				canvasPanel.Panel.AddParentButton();
				canvasPanel.Panel.Title = "LogiX Cleanup Wizard";
				canvasPanel.Canvas.Size.Value = new float2(400f, 300f);

				Slot Data = WizardSlot.AddSlot("Data");
				this.processingRoot = Data.AddSlot("processingRoot").AttachComponent<ReferenceField<Slot>>();
				removeLogixReferences = Data.AddSlot("removeLogixReferences").AttachComponent<ValueField<bool>>();
				removeLogixInterfaceProxies = Data.AddSlot("removeLogixInterfaceProxies").AttachComponent<ValueField<bool>>();

				UIBuilder UI = new UIBuilder(canvasPanel.Canvas);
				UI.Canvas.MarkDeveloper();
				UI.Canvas.AcceptPhysicalTouch.Value = false;
				VerticalLayout verticalLayout = UI.VerticalLayout(4f, childAlignment: Alignment.TopCenter);
				verticalLayout.ForceExpandHeight.Value = false;
				UI.Style.MinHeight = 24f;
				UI.Style.PreferredHeight = 24f;

				UI.Text("Processing Root:").HorizontalAlign.Value = TextHorizontalAlignment.Left;
				UI.Next("Root");
				UI.Current.AttachComponent<RefEditor>().Setup(processingRoot.Reference);

				UI.HorizontalElementWithLabel("Remove Unused LogixReferences:", 0.942f, () => UI.BooleanMemberEditor(removeLogixReferences.Value));
				UI.HorizontalElementWithLabel("Remove LogixInterfaceProxies:", 0.942f, () => UI.BooleanMemberEditor(removeLogixInterfaceProxies.Value));

				cleanUnusedLogixComponents = UI.Button("Cleanup Unused Logix Components");
				cleanUnusedLogixComponents.LocalPressed += CleanupLogix;

				destroyInterfaces = UI.Button("Destroy Interfaces");
				destroyInterfaces.LocalPressed += DestroyInterfaces;

				removeEmptyRefs = UI.Button("Remove Empty Refs");
				removeEmptyRefs.LocalPressed += RemoveEmptyRefs;

				removeEmptyCasts = UI.Button("Remove Empty Casts");
				removeEmptyCasts.LocalPressed += RemoveEmptyCasts;

				processingRoot.Reference.Value = WizardSlot.World.RootSlot.ReferenceID;
				removeLogixReferences.Value.Value = true;
				removeLogixInterfaceProxies.Value.Value = false;

				UI.Text("Status:");
				statusText = UI.Text("");

				WizardSlot.PositionInFrontOfUser(float3.Backward, distance: 1f);
			}

			void Slot_OnPrepareDestroy(Slot slot) {
				_Wizard = null;
			}

			void CleanupLogix(IButton button, ButtonEventData eventData) {
				UpdateStatusText("Cleaning up LogiX");
				WizardSlot.World.Coroutines.StartTask(async () => {
					int totalRemovedComponents = await OptimizeLogiX(processingRoot.Reference, removeLogixReferences.Value, removeLogixInterfaceProxies.Value);
					Msg($"Removed {totalRemovedComponents} components");
					UpdateStatusText($"Removed {totalRemovedComponents} components");
				});
			}

			public static async Task<int> OptimizeLogiX(Slot targetSlot, bool removeLogixReferences, bool removeLogixInterfaceProxies) {
				if (targetSlot == null) {
					return 0;
				}
				await new Updates(10);
				List<Component> componentsForRemoval = targetSlot.GetComponentsInChildren((Component targetComponent) => {
					//Collect all LogiXReference and LogixInterfaceProxies for deletion
					if (removeLogixReferences && targetComponent is LogixReference) {
						return true;
					}
					if (removeLogixInterfaceProxies && targetComponent is LogixInterfaceProxy) {
						return true;
					}
					return false;
				});

				foreach (Component targetComponent in componentsForRemoval) {
					targetComponent.Destroy();
				}
				return componentsForRemoval.Count;
			}

			void DestroyInterfaces(IButton button, ButtonEventData eventData) {
				WizardSlot.World.Coroutines.StartTask(async () => {
					List<LogixInterface> interfaces = WizardSlot.World.RootSlot.GetComponentsInChildren<LogixInterface>();
					if (interfaces == null) {
						return;
					}
					int interfacesCount = interfaces.Count;
					foreach (LogixInterface @interface in interfaces) {
						@interface.Slot.Destroy();
					}
					Msg($"Destroyed {interfacesCount} Interfaces");
					UpdateStatusText($"Destroyed {interfacesCount} Interfaces");
				});
			}

			void RemoveEmptyRefs(IButton button, ButtonEventData eventData) {
				//Search for Slots named Ref with no components and no children
				WizardSlot.World.Coroutines.StartTask(async () => {
					List<Slot> refSlots = processingRoot.Reference.Target.GetAllChildren();
					UpdateStatusText($"Searching {refSlots.Count()} Slots");
					await new Updates(10);
					var removalCount = 0;
					foreach (Slot @ref in refSlots) {
						try {
							if (!(String.IsNullOrEmpty(@ref.Name))) {
								if (@ref.Name.Contains("Ref") && @ref.ChildrenCount == 0 && @ref.ComponentCount == 0) {
									@ref.Destroy();
									removalCount++;
								}
							}
						} catch (Exception e) {
							Msg(e);
						}
					}
					UpdateStatusText($"Removed {removalCount} Empty Refs");
				});
			}

			void RemoveEmptyCasts(IButton button, ButtonEventData eventData) {
				//Search for Slots named Cast with no components and no children
				WizardSlot.World.Coroutines.StartTask(async () => {
					List<Slot> refSlots = processingRoot.Reference.Target.GetAllChildren();
					UpdateStatusText($"Searching {refSlots.Count()} Slots");
					await new Updates(10);
					var removalCount = 0;
					foreach (Slot @ref in refSlots) {
						try {
							if (!(String.IsNullOrEmpty(@ref.Name))) {
								if (@ref.Name.Contains("Cast") && @ref.ChildrenCount == 0 && @ref.ComponentCount == 0) {
									@ref.Destroy();
									removalCount++;
								}
							}
						} catch (Exception e) {
							Msg(e);
						}
					}
					UpdateStatusText($"Removed {removalCount} Empty Casts");
				});
			}
		}
	}
}