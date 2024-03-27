using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using WolvenKit.App.Helpers;
using WolvenKit.App.Models;
using WolvenKit.Core.Extensions;
using WolvenKit.RED4.Types;

namespace WolvenKit.App.ViewModels.Shell;

public partial class ChunkViewModel
{
    [MemberNotNull(nameof(Value))]
    public void CalculateValue()
    {
        Value = Data is null ? "null" : "";

        // nothing to calculate
        if (ResolvedData is RedDummy)
        {
            return;
        }

        if (PropertyType.IsAssignableTo(typeof(IRedString)) && Data is IRedString redString)
        {
            var value = redString.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                Value = value;
                if (Value.StartsWith("LocKey#") && ulong.TryParse(Value[7..], out var key))
                {
                    Value = "";
                }
            }
        }
        else if (PropertyType.IsAssignableTo(typeof(CByteArray)) && Data is CByteArray b)
        {
            var ba = (byte[])b;
            Value = string.Join(" ", ba.Select(x => $"{x:X2}"));
        }
        else if (PropertyType.IsAssignableTo(typeof(LocalizationString)) && Data is LocalizationString l)
        {
            var value = l;
            Value = value.Value is "" or null ? "null" : value.Value;
        }
        else if (PropertyType.IsAssignableTo(typeof(IRedEnum)) && Data is IRedEnum e)
        {
            var value = e;
            Value = value.ToEnumString();
        }
        else if (PropertyType.IsAssignableTo(typeof(IRedBitField)) && Data is IRedBitField f)
        {
            var value = f;
            Value = value.ToBitFieldString();
        }
        else if (NodeIdxInParent > -1 && Parent?.Name == "referenceTracks" &&
                 GetRootModel().GetModelFromPath("trackNames")?.ResolvedData is CArray<CName> trackNames)
        {
            Value = trackNames[NodeIdxInParent].GetResolvedText();
            IsValueExtrapolated = true;
        }
        //else if (PropertyType.IsAssignableTo(typeof(TweakDBID)))
        //{
        //    Value = (TweakDBID)Data.ToString();
        //    //Value = Locator.Current.GetService<TweakDBService>().GetString(value);
        //}
        else if (PropertyType.IsAssignableTo(typeof(CBool)) && Data is CBool cb)
        {
            var value = cb;
            Value = value ? "True" : "False";
        }
        else if (PropertyType.IsAssignableTo(typeof(CRUID)) && Data is CRUID cr)
        {
            var value = cr;
            Value = ((ulong)value).ToString();
        }
        else if (PropertyType.IsAssignableTo(typeof(CUInt64)) && Data is CUInt64 uInt64)
        {
            var value = uInt64;
            Value = value != 0 ? ((NodeRef)(ulong)value).ToString() : ((ulong)value).ToString();
        }
        else if (PropertyType.IsAssignableTo(typeof(gamedataLocKeyWrapper)))
        {
            //var value = (gamedataLocKeyWrapper)Data;
            //Value = ((ulong)value).ToString();
            //Value = Locator.Current.GetService<LocKeyService>().GetFemaleVariant(value);
        }
        else if (PropertyType.IsAssignableTo(typeof(IRedInteger)) && Data is IRedInteger i)
        {
            var value = i;

            Value = value.ToString(CultureInfo.CurrentCulture);
        }
        else if (PropertyType.IsAssignableTo(typeof(FixedPoint)) && Data is FixedPoint fp)
        {
            var value = fp;
            Value = ((float)value).ToString("G9");
        }
        else if (PropertyType.IsAssignableTo(typeof(NodeRef)) && Data is NodeRef nr)
        {
            var value = nr;
            Value = value;
        }
        else if (PropertyType.IsAssignableTo(typeof(IRedRef)) && Data is IRedRef rr)
        {
            // if a path is resolved in the file, but is not yet added to the list of known hashes, add it
            var depotPath = rr.DepotPath;
            if (!_hashService.Contains(depotPath) && !ResourcePath.IsNullOrEmpty(depotPath))
            {
                _hashService.AddCustom(depotPath.GetResolvedText() ?? "");
            }

            if (depotPath.IsResolvable)
            {
                Value = depotPath.GetResolvedText().NotNull();
            }
            else
            {
                Value = depotPath == ResourcePath.Empty
                    ? "null"
                    : $"{(ulong)depotPath}{_hashService.GetGuessedExtension(depotPath)}";
            }
        }
        else if (Data is IBrowsableType ibt)
        {
            Value = ibt.GetBrowsableValue();
        }
        else if (ResolvedData is animPoseLink link)
        {
            if (link.Node is not null)
            {
                var desc = GetNodeName(link.Node);

                if (!string.IsNullOrEmpty(desc))
                {
                    Value = desc;
                    IsValueExtrapolated = true;
                    return;
                }
            }
        }

        // factory.csv
        else if (Parent is { Name: "compiledData" } && GetRootModel().Data is C2dArray &&
                 Data is CArray<CString> { Count: 3 } ary)
        {
            IsValueExtrapolated = true;
            Value = ary[1];
        }
        // i18n.json
        else if (Data is localizationPersistenceOnScreenEntry i18n)
        {
            IsValueExtrapolated = true;
            // fall back to male variant only if female variant is
            Value = i18n.FemaleVariant;
            if (Value == "" && i18n.MaleVariant != "")
            {
                Value = i18n.MaleVariant;
            }
        }
        // i18n.json
        else if (Data is IRedBaseHandle handle && handle.GetValue() is scnSceneGraphNode sgn)
        {
            IsValueExtrapolated = true;
            Value = StringHelper.Stringify(sgn.OutputSockets);
        }


        switch (ResolvedData)
        {
            case CKeyValuePair kvp:
                // If the CValuePair has a value, we'll try to resolve it
                Value = kvp.Value switch
                {
                    CName cname => cname.GetResolvedText() ?? "",
                    CResourceReference<ITexture> reference => reference.DepotPath.GetResolvedText() ?? "",
                    _ => kvp.Value.ToString()
                };
                IsValueExtrapolated = true;
                break;
            case meshMeshAppearance { ChunkMaterials: not null } appearance:
                Value = string.Join(", ", appearance.ChunkMaterials);
                Value = $"[{appearance.ChunkMaterials.Count}] {Value}";
                IsValueExtrapolated = true;
                break;
            // Material instance (mesh): "[2] - engine\materials\multilayered.mt" (show #keyValuePairs)
            case CMaterialInstance { BaseMaterial: { } cResourceReference } material:
            {
                var numMaterials = $"[{material.Values?.Count ?? 0}] - ";
                Value = $"{numMaterials}{cResourceReference.DepotPath.GetResolvedText() ?? "none"}";
                IsValueExtrapolated = Value != "";
                break;
            }
            case CResourceAsyncReference<IMaterial> materialRef
                when materialRef.DepotPath.GetResolvedText() is string text:
                Value = text;
                IsValueExtrapolated = Value != "";
                break;
            case scnSceneWorkspotDataId sceneWorkspotData when sceneWorkspotData.Id != 0:
                Value = $"{sceneWorkspotData.Id}";
                IsValueExtrapolated = sceneWorkspotData.Id != 0;
                break;
            case scnSceneWorkspotInstanceId sceneWorkspotInstance when sceneWorkspotInstance.Id != 0:
                Value = $"{sceneWorkspotInstance.Id}";
                IsValueExtrapolated = sceneWorkspotInstance.Id != 0;
                break;
            case scnNotablePoint scnNotablePoint when scnNotablePoint.NodeId.Id != 0:
                Value = $"NodeId: {scnNotablePoint.NodeId.Id}";
                IsValueExtrapolated = true;
                break;
            case scnActorId scnActorId:
                Value = $"{scnActorId.Id}";
                IsValueExtrapolated = scnActorId.Id != 0;
                break;
            case scnPlayerActorDef playerActorDef:
                Value = $"NodeId: {playerActorDef.SpecCharacterRecordId.GetResolvedText()}";
                IsValueExtrapolated = true;
                break;
            case workWorkEntryId id:
                Value = $"{id.Id}";
                IsValueExtrapolated = true;
                break;

            case scnCinematicAnimSetSRRef scnCineAnimRef:
                Value = $"{scnCineAnimRef.AsyncAnimSet.DepotPath.GetResolvedText()}";
                IsValueExtrapolated = Value != "";
                if (scnCineAnimRef.IsOverride)
                {
                    var separator = Value == "" ? "" : " | ";
                    Value = $"{Value}{separator}IsOverride: true";
                }

                return;
            case scnVoicesetComponent voiceset:
                Value = voiceset.CombatVoSettingsName.GetResolvedText() ?? "";
                IsValueExtrapolated = Value != "";
                break;
            case entSoundListenerComponent listener when
                listener.ParentTransform?.GetValue() is entHardTransformBinding tBinding:
                Value = StringHelper.Stringify(tBinding);
                IsValueExtrapolated = Value != "";
                break;
            case entSlotComponent slotComponent when
                slotComponent.ParentTransform?.GetValue() is entHardTransformBinding tBinding4:
                Value = StringHelper.Stringify(tBinding4);
                IsValueExtrapolated = Value != "";
                break;
            case gameaudioSoundComponent soundComponent:
                Value = $"{soundComponent.AudioName}";
                IsValueExtrapolated = Value != "";
                break;
            case WidgetHudComponent hudComponent:
                Value = $"{hudComponent.HudEntriesResource}";
                IsValueExtrapolated = Value != "";
                break;
            case WidgetMenuComponent menuComponent:
                Value = $"{menuComponent.CursorResource}";
                IsValueExtrapolated = Value != "";
                break;
            case entTriggerComponent triggerComponent when
                triggerComponent.ParentTransform?.GetValue() is entHardTransformBinding tBinding2:
                Value = StringHelper.Stringify(tBinding2);
                IsValueExtrapolated = Value != "";
                break;
            case scnlocLocstringId stringId when
                stringId.Ruid != 0:
                Value = stringId.Ruid.ToString();
                IsValueExtrapolated = true;
                break;
            case scnEntryPoint entryPoint:
                Value = $"NodeID: {entryPoint.NodeId.Id}";
                IsValueExtrapolated = true;
                break;
            case scnExitPoint exitPoint:
                Value = $"NodeID: {exitPoint.NodeId.Id}";
                IsValueExtrapolated = true;
                break;
            case scnlocVariantId variantId:
                Value = variantId.Ruid.ToString();
                break;
            case scnPerformerId performerId:
                Value = performerId.Id.ToString();
                break;
            case CArray<CName> cNames:
                Value = StringHelper.Stringify(cNames);
                IsValueExtrapolated = cNames.Count != 0;
                break;
            case CArray<TweakDBID> tweakIds:
                Value = StringHelper.Stringify(tweakIds);
                IsValueExtrapolated = tweakIds.Count != 0;
                break;
            case scnInterruptionScenarioId scenarioId:
                Value = scenarioId.Id.ToString();
                break;
            case scnscreenplayItemId scnscreenplayItemId:
                Value = scnscreenplayItemId.Id.ToString();
                break;
            case scnscreenplayDialogLine scnscreenplayDialogLine:
                Value = scnscreenplayDialogLine.LocstringId.Ruid.ToString();
                IsValueExtrapolated = scnscreenplayDialogLine.LocstringId.Ruid != 0;
                break;
            case scnWorkspotInstance workspotInstance:
                Value = $"{workspotInstance.WorkspotInstanceId.Id}";
                IsValueExtrapolated = workspotInstance.WorkspotInstanceId.Id != 0;
                break;
            case scnWorkspotData_ExternalWorkspotResource externalWorkspotResource:
                Value = externalWorkspotResource.WorkspotResource.DepotPath.GetResolvedText();
                IsValueExtrapolated =
                    externalWorkspotResource.WorkspotResource.DepotPath.GetResolvedText() is not (null or "" or "none");
                break;
            case scnWorkspotData_EmbeddedWorkspotTree externalWorkspotTree:
                Value = externalWorkspotTree.DataId.Id.ToString();
                IsValueExtrapolated = externalWorkspotTree.DataId.Id != 0;
                break;
            case scnGenderMask scnGenderMask:
                Value = scnGenderMask.Mask.ToString();
                IsValueExtrapolated = scnGenderMask.Mask != 0;
                break;
            case scnPerformerSymbol performerSymbol:
                Value = performerSymbol.PerformerId.Id.ToString();
                IsValueExtrapolated = performerSymbol.PerformerId.Id != 0;
                break;
            case scnSceneEventSymbol sceneEventSymbol:
                Value = sceneEventSymbol.OriginNodeId.Id.ToString();
                IsValueExtrapolated = sceneEventSymbol.OriginNodeId.Id != 0;
                break;
            case scnWorkspotSymbol scnWorkspotSymbol:
                Value = scnWorkspotSymbol.WsEditorEventId.ToString();
                IsValueExtrapolated = scnWorkspotSymbol.WsEditorEventId != 0;
                break;
            case scnCinematicAnimSetSRRefId cinematicAnimSetRefId:
                Value = cinematicAnimSetRefId.Id.ToString();
                break;
            case scnlocSignature locSignature:
                Value = locSignature.Val.ToString();
                IsValueExtrapolated = locSignature.Val != 0;
                break;
            case gameEntityReference gameEntRef:
                Value = gameEntRef.Reference.GetResolvedText();
                IsValueExtrapolated = Value != "";
                break;
            case scnPropDef propDef when propDef.FindEntityInNodeParams.NodeRef.GetResolvedText() is string nodeRef &&
                                         nodeRef != "":
                Value = $"NodeRef: {nodeRef}";
                IsValueExtrapolated = true;
                return;
            case scnAnimSetAnimNames animNames when animNames.AnimationNames.Count > 0:
                Value = $"{StringHelper.Stringify(animNames.AnimationNames)}";
                IsValueExtrapolated = true;
                return;
            case scnInputSocketId socketId:
                Value = $"{socketId.NodeId.Id}";
                IsValueExtrapolated = socketId.NodeId.Id != 0;
                return;
            case ICollection<scnInputSocketId> socketIds:
                Value = $"{StringHelper.Stringify(socketIds.Select(s => s.NodeId.Id.ToString()).ToArray())}";
                IsValueExtrapolated = Value != "";
                return;
            case scnChoiceNodeOption scnChoiceNodeOption:
                Value = $"{scnChoiceNodeOption.Caption.GetResolvedText()}";
                IsValueExtrapolated = Value != "";
                return;
            case scnscreenplayOptionUsage screenplayOptionUsage:
                Value = $"{screenplayOptionUsage.PlayerGenderMask.Mask}";
                IsValueExtrapolated = screenplayOptionUsage.PlayerGenderMask.Mask == 0;
                IsValueExtrapolated = !IsDefault;
                return;
            case scnscreenplayChoiceOption screenplayOption:
                Value = $"{screenplayOption.ItemId.Id} => {screenplayOption.LocstringId.Ruid}";
                IsValueExtrapolated = screenplayOption.ItemId.Id != 0 || screenplayOption.LocstringId.Ruid != 0;
                return;
            case scnSpawnDespawnEntityParams spawnDespawnParams
                when spawnDespawnParams.SpecRecordId.GetResolvedText() is string specRecordId && specRecordId != "":
                Value = $"{specRecordId}";
                IsValueExtrapolated = true;
                return;
            case Transform transform:
                Value = $"{StringHelper.Stringify(transform.Position)}";
                IsValueExtrapolated = Value != "";
                return;
            case scnPropId propId:
                Value = $"{propId.Id}";
                IsValueExtrapolated = propId.Id != 0;
                return;
            case scnSceneSolutionHash scnSolutionHash:
                Value = $"{scnSolutionHash.SceneSolutionHash.SceneSolutionHashDate}";
                IsValueExtrapolated = scnSolutionHash.SceneSolutionHash.SceneSolutionHashDate != 0;
                return;
            case scnSceneSolutionHashHash scnSolutionHashHash:
                Value = $"{scnSolutionHashHash.SceneSolutionHashDate}";
                IsValueExtrapolated = scnSolutionHashHash.SceneSolutionHashDate != 0;
                return;
            case scnGameplayAnimSetSRRef animSetRRef:
                Value = $"{animSetRRef.AsyncAnimSet.DepotPath.GetResolvedText()}";
                IsValueExtrapolated = Value != "";
                return;
            case scnFindEntityInNodeParams findInParams
                when findInParams.NodeRef.GetResolvedText() is string s && s != "":
                Value = $"NodeRef: {s}";
                return;
            case scnFindEntityInNodeParams nodeParams
                when nodeParams.NodeRef.GetResolvedText() is string s && s != "":
                Value = $"NodeRef: {s}";
                return;
            case entTriggerActivatorComponent tActivatorComponent:
            {
                if (tActivatorComponent.ParentTransform?.GetValue() is entHardTransformBinding tBinding3)
                {
                    Value = StringHelper.Stringify(tBinding3);
                }

                if (tActivatorComponent.Channels.ToString().Length > 0)
                {
                    var separator = Value == "" ? "" : " -> ";
                    Value = $"{Value}{separator}{tActivatorComponent.Channels}";
                }

                IsValueExtrapolated = Value != "";
                break;
            }
            case gameinteractionsComponent intComponent:
                Value = $"{intComponent.DefinitionResource}";
                IsValueExtrapolated = Value != "";
                break;
            case FxResourceMapData mapData when mapData.Resource is gameFxResource fxResourceValue:
                Value = fxResourceValue.Effect.DepotPath.GetResolvedText();
                IsValueExtrapolated = Value != "";
                break;
            case entMeshComponent meshComponent:
            {
                Value = "";
                if (meshComponent.ParentTransform?.GetValue() is entHardTransformBinding parentTransformValue)
                {
                    Value = StringHelper.Stringify(parentTransformValue);
                }

                if (meshComponent.Mesh.DepotPath.GetResolvedText() is string dePathText)
                {
                    Value = Value.Length == 0 ? $"{dePathText}" : $" ({dePathText})";
                }

                IsValueExtrapolated = Value != "";
                break;
            }
            case entSkinnedMeshComponent skinnedMeshComponent:
            {
                Value = "";
                if (skinnedMeshComponent.ParentTransform?.GetValue() is entHardTransformBinding parentTransformValue)
                {
                    Value = StringHelper.Stringify(parentTransformValue);
                }

                if (skinnedMeshComponent.Mesh.DepotPath.GetResolvedText() is string dePathText)
                {
                    Value = Value.Length == 0 ? $"{dePathText}" : $" ({dePathText})";
                }

                IsValueExtrapolated = Value != "";
                break;
            }
            case entDynamicActorRepellingComponent repComponent when
                repComponent.ParentTransform?.GetValue() is entHardTransformBinding parentTransformValue:
                Value = StringHelper.Stringify(parentTransformValue);
                IsValueExtrapolated = Value != "";
                break;
            case gameFxResource fxResource:
                Value = fxResource.Effect.DepotPath.GetResolvedText();
                IsValueExtrapolated = Value != "";
                break;
            case senseVisibleObjectComponent visibleComponent when
                visibleComponent.VisibleObject?.GetValue() is senseVisibleObject visibleObject:
                Value = $"{visibleObject.Description}";
                IsValueExtrapolated = Value != "";
                break;
            case workWorkspotAnimsetEntry animsetEntry:
                Value = $"{animsetEntry.Rig.DepotPath.GetResolvedText() ?? "none"}";
                IsValueExtrapolated = true;
                break;
            case gameAudioEmitterComponent audioEmitter:
                Value = $"{audioEmitter.EmitterName}";
                IsValueExtrapolated = Value != "";
                break;
            case CMeshMaterialEntry materialDefinition:
                Value = materialDefinition.IsLocalInstance ? "" : " (external)";
                Value = $"{materialDefinition.Index}{Value}";
                IsValueExtrapolated = true;
                break;
            case animRigRetarget retarget:
                Value = $"{retarget.SourceRig}";
                IsValueExtrapolated = true;
                break;
            case redTagList list:
                Value = $"[ {string.Join(", ", list.Tags.ToList().Select(t => t.GetResolvedText() ?? "").ToArray())} ]";
                IsValueExtrapolated = true;
                break;
            case physicsRagdollBodyInfo when
                NodeIdxInParent > -1 && GetRootModel().GetModelFromPath("ragdollNames")?.ResolvedData is
                    CArray<physicsRagdollBodyNames> ragdollNames:
                var rN = ragdollNames[NodeIdxInParent];
                Value = $"{rN.ParentAnimName.GetResolvedText() ?? ""} -> {rN.ChildAnimName.GetResolvedText() ?? ""}";
                IsValueExtrapolated = true;
                break;
            case scnNodeSymbol scnNodeSymbol when scnNodeSymbol.EditorNodeId.Id != 0:
                Value = $"EditorNodeId: {scnNodeSymbol.EditorNodeId.Id}";
                IsValueExtrapolated = true;
                break;
            default:
                break;
        }

        // Make sure it's never null
        Value ??= "null";
    }

}