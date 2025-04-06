namespace TkSharp.Merging.ChangelogBuilders.BinaryYaml;

public class BymlArrayChangelogBuilderProvider : Singleton<BymlArrayChangelogBuilderProvider>, IBymlArrayChangelogBuilderProvider
{
    // ReSharper disable StringLiteralTypo
    public IBymlArrayChangelogBuilder GetChangelogBuilder(ref BymlTrackingInfo info, ReadOnlySpan<char> key)
    {
        return key switch {
            "Animal" or "Enemy" or "FallFloorInsect" or "Fish" or "GrassCut" or "Insect" or "NotDecayedLargeSwordList"
                or "NotDecayedSmallSwordList" or "NotDecayedSpearList" or "RainBonusMaterial" or "Seafood"
                or "SpObjCapsuleBlockMaster" or "Weapon" or "bow" or "bows" or "shields" or "weapons"
                or "helmets" => new BymlKeyedArrayChangelogBuilder("name"),
            "Actors" => info.Type switch {
                "bcett" => new BymlKeyedArrayChangelogBuilder("Hash"),
                "game__component__ArmyManagerParam" => new BymlKeyedArrayChangelogBuilder("ActorName"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "Table" => info.Type switch {
                "game__colorvariation__ConversionActorNameToParasailPatternSetTable"
                    => new BymlKeyedArrayChangelogBuilder("ParasailPattern"),
                "game__ecosystem__DecayedWeaponMappingTable"
                    => new BymlKeyedArrayChangelogBuilder("EquipmentDeathCountGmdHash"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "BoneInfoArray" => info.Type switch {
                "game__component__DragonParam" => new BymlKeyedArrayChangelogBuilder("Hash"),
                "phive__LookIKResourceHeaderParam"
                    or "phive__TailBoneResourceHeaderParam" => new BymlKeyedArrayChangelogBuilder("BoneName"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "Elements" => info.Type switch {
                "game__enemy__DrakeSubModelInfo" => new BymlKeyedArrayChangelogBuilder("BoneName"),
                "game__gamebalance__LevelSensorTargetDefine" => new BymlKeyedArrayChangelogBuilder("ActorNameHash", "Plus"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "Items" => info.Type switch {
                "game__pouchcontent__EnhancementMaterial" => new BymlKeyedArrayChangelogBuilder("Actor"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "List" => info.Type switch {
                "game__sound__ShrineSpotBgmTypeInfoList" => new BymlKeyedArrayChangelogBuilder("DungeonIndexStr"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "Contents" => info.Type switch {
                "game__ui__FairyFountainGlobalSetting" => new BymlKeyedArrayChangelogBuilder("Actor"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "SettingTable" => info.Type switch {
                "game__ui__LargeDungeonFloorDefaultSettingTable" => new BymlKeyedArrayChangelogBuilder("DungeonType"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "BrainVerbs" => new BymlKeyedArrayChangelogBuilder("ActionSeqContainer"),
            "ActionVerbContainerElements" => new BymlKeyedArrayChangelogBuilder("ActionVerb"),
            "ResidentActors" or "Settings"
                or "ShootableShareActorSettings" or "GoodsList" => new BymlKeyedArrayChangelogBuilder("Actor"),
            "BindActorInfo" => new BymlKeyedArrayChangelogBuilder("ActorHolderKey"),
            "RegisteredActorArray" or "RequirementList"
                or "Rewards" => new BymlKeyedArrayChangelogBuilder("ActorName"),
            "SharpInfoList" or "SharpInfoBowList" or "SharpInfoShieldList" => new BymlKeyedArrayChangelogBuilder("ActorNameHash"),
            "PictureBookParamArray" => new BymlKeyedArrayChangelogBuilder("ActorNameShort"),
            "WeakPointActorArray" => new BymlKeyedArrayChangelogBuilder("ActorPath"),
            "NavMeshObjects" => new BymlKeyedArrayChangelogBuilder("Alias"),
            "AliasEntityList" => new BymlKeyedArrayChangelogBuilder("AliasEntity"),
            "AliasSensorList" => new BymlKeyedArrayChangelogBuilder("AliasSensor"),
            "Anchors" => new BymlKeyedArrayChangelogBuilder("AnchorName"),
            "ArmorEffect" => new BymlKeyedArrayChangelogBuilder("ArmorEffectType"),
            "HornTypeAndAttachmentMapping" => new BymlKeyedArrayChangelogBuilder("AttachmentName"),
            "AttackParams" => new BymlKeyedArrayChangelogBuilder("AttackType"),
            "BlackboardParamBoolArray" or "BlackboardParamCustomTypeArray" or "BlackboardParamF32Array"
                or "BlackboardParamMtx33fArray" or "BlackboardParamMtx34fArray" or "BlackboardParamPtrArray"
                or "BlackboardParamQuatfArray" or "BlackboardParamS32Array" or "BlackboardParamS8Array"
                or "BlackboardParamStringArray" or "BlackboardParamU32Array" or "BlackboardParamU64Array"
                or "BlackboardParamU8Array" or "BlackboardParamVec3fArray"
                or "EditBBParams" => new BymlKeyedArrayChangelogBuilder("BBKey"),
            "DragonInfoList" => new BymlKeyedArrayChangelogBuilder("BindPointRespawnGameDataHash"),
            "OperationAngular" or "OperationLinear" => new BymlKeyedArrayChangelogBuilder("Body"),
            "BindBoneList" or "BoneList" or "BoneModifierSet" or "Bones" or "FruitOffsetTranslation"
                or "ModelBindSettings" or "StickWeaponBone" => new BymlKeyedArrayChangelogBuilder("BoneName"),
            "Categories" => new BymlKeyedArrayChangelogBuilder("CallbackName"),
            "CaveParams" => new BymlKeyedArrayChangelogBuilder("CaveInstanceId"),
            "CheckList" => new BymlKeyedArrayChangelogBuilder("CheckType"),
            "Object" => new BymlKeyedArrayChangelogBuilder("ChemicalMaterial"),
            "CharacterComponentPresetCollection" or "LayerHitMaskEntityCollection" or "LayerHitMaskSensorCollection"
                or "MaterialCollection" or "MaterialPresetCollection" or "MotionPropertiesCollection"
                or "PhysicsMaterialMappingInfoCollection" or "SubLayerHitMaskEntityCollection"
                or "SubLayerHitMaskSensorCollection"
                or "UserShapeTagMaskCollection" => new BymlKeyedArrayChangelogBuilder("ComponentName"),
            "HingeArray" or "RangeArray" => new BymlKeyedArrayChangelogBuilder("ConstraintName"),
            "CropYieldTable" => new BymlKeyedArrayChangelogBuilder("CropName"),
            "DungeonBossDifficultyGameData" => new BymlKeyedArrayChangelogBuilder("DefeatedNumGameDataHash"),
            "DoCondition" or "FinCondition" or "PickConditions"
                or "SuccessCondition" => new BymlKeyedArrayChangelogBuilder("DefineNameHash"),
            "FallenActorTable" => new BymlKeyedArrayChangelogBuilder("DropActorName"),
            "SmallDungeonLocationList" => new BymlKeyedArrayChangelogBuilder("DungeonIndexStr"),
            "VariationListForArmorDye" => new BymlKeyedArrayChangelogBuilder("DyeColor"),
            "HackEquip" => new BymlKeyedArrayChangelogBuilder("EquipUserBbKey"),
            "AdventureMemorySetArray"
                or "GlobalResidentEventList" => new BymlKeyedArrayChangelogBuilder("EventName"),
            "AutoPlayBoneVisibilities" or "AutoPlayMaterials" => new BymlKeyedArrayChangelogBuilder("FileName"),
            "WinningRateTable" => new BymlKeyedArrayChangelogBuilder("FlintstonesNum"),
            "ModelVariationAnims" => new BymlKeyedArrayChangelogBuilder("Fmab"),
            "OptionParam" => new BymlKeyedArrayChangelogBuilder("FootIKMode"),
            "PartialList" => new BymlKeyedArrayChangelogBuilder("GameData"),
            "PlacementGroups" => new BymlKeyedArrayChangelogBuilder("GroupID"),
            "EffectLimiterGroup" or "HiddenMaterialGroupList" => new BymlKeyedArrayChangelogBuilder("GroupName"),
            "Textures" => new BymlKeyedArrayChangelogBuilder("guid"),
            /* "AiGroups" or */ "Points" => new BymlKeyedArrayChangelogBuilder("Hash"),
            "Rails" => info.Depth switch {
                0 => new BymlKeyedArrayChangelogBuilder("Hash"),
                _ => BymlArrayChangelogBuilder.Instance
            },
            "HeadshotDamageParameters" => new BymlKeyedArrayChangelogBuilder("HeadshotBoneName"),
            "TransitionParam" => new BymlKeyedArrayChangelogBuilder("Index"),
            "OverwriteParam" => new BymlKeyedArrayChangelogBuilder("InstanceId"),
            "Interests" => new BymlKeyedArrayChangelogBuilder("InterestType"),
            "StrongInterests" => new BymlKeyedArrayChangelogBuilder("InterestType"),
            "ConditionArray" or "OverrideASEvReactVerbSettings" or "OverrideASEventReactSettings" or "SwitchParam"
                or "TriggerParams" or "Triggers" => new BymlKeyedArrayChangelogBuilder("Key"),
            "OverrideReactionVerbSettings" => new BymlKeyedArrayChangelogBuilder("KeyActionVerb"),
            "ShootableActorSettings" => new BymlKeyedArrayChangelogBuilder("KeyHash", "Actor"),
            "AttachmentGroupList" or "EnemyGroupList" => new BymlKeyedArrayChangelogBuilder("Label"),
            "ShopWeaponGroupList" or "WeaponGroupList" => new BymlKeyedArrayChangelogBuilder("Label", "EquipmentType"),
            "ActionSeqs" => new BymlKeyedArrayChangelogBuilder("LabelHash"),
            "CaveEntranceNormal" or "CaveEntranceSpecial" or "CaveEntranceWell" or "CheckPoint" or "City"
                or "District" or "DragonTears" or "Dungeon" or "Ground" or "ShopArmor" or "ShopDye" or "ShopGeneral"
                or "ShopInn" or "ShopJewelry" or "Shrine" or "SkyArchipelago" or "SpotBig" or "SpotBigArtifact"
                or "SpotBigMagma" or "SpotBigMountain" or "SpotBigOther" or "SpotBigTimber" or "SpotBigWater"
                or "SpotBigWithNameIcon" or "SpotMiddle" or "SpotMiddleArtifact" or "SpotMiddleMagma"
                or "SpotMiddleMountain" or "SpotMiddleOther" or "SpotMiddleTimber" or "SpotMiddleWater"
                or "SpotSmallArtifact" or "SpotSmallMagma" or "SpotSmallMountain" or "SpotSmallOther"
                or "SpotSmallTimber" or "SpotSmallWater" or "Stable" or "Tower"
                or "Underground" => info.Type switch {
                    "locationarea" => new BymlKeyedArrayChangelogBuilder("LocationName"),
                    _ => BymlArrayChangelogBuilder.Instance
                },
            "MiasmaAreaParam" => new BymlKeyedArrayChangelogBuilder("MiasmaAreaType"),
            "AnimationDrive" or "CColEntityNamePathAry" or "CColSensorNamePathAry" or "CheckPointSetting" or "Cloth"
                or "ClothAdvandecOption" or "ClothList" or "ClothReaction" or "CollectItem" or "CollidableList"
                or "ControllerEntityNamePathAry" or "ControllerSensorNamePathAry" or "ControllerSensorUnitAry"
                or "ExternalShapeNamePathAry" or "HelperBoneList" or "IntData" or "LookIKControllerNamePathAry"
                or "LookingControllerNamePathAry" or "MatterRigidBodyNamePathAry" or "MeshList" or "ParamTable"
                or "RagdollReaction" or "RagdollReactionList" or "RagdollStructure" or "Reaction"
                or "RigidBodyEntityNamePathAry" or "RigidBodySensorNamePathAry" or "ShapeList" or "ShapeNamePathAry"
                or "StringData" or "TailBoneControllerNamePathAry"
                or "Node" => new BymlKeyedArrayChangelogBuilder("Name"),
            "TowingHookParams" => new BymlKeyedArrayChangelogBuilder("NameHash"),
            "Property" => new BymlNameHashArrayChangelogBuilder(),
            "ExtraNewsSourceInfo" or "TopNewsSourceInfo" => new BymlKeyedArrayChangelogBuilder("NewsKeyName"),
            "PictureBookPackInfoArray" => new BymlKeyedArrayChangelogBuilder("PackActor"),
            "VariationListForParasail" => new BymlKeyedArrayChangelogBuilder("Pattern"),
            "PhshMesh" => new BymlKeyedArrayChangelogBuilder("PhshMeshPath"),
            "PlgGdTable" => new BymlKeyedArrayChangelogBuilder("PlgGuid"),
            "PropertyDefinitions" or "Sources" => new BymlKeyedArrayChangelogBuilder("PropertyNameHash"),
            "Connections" => new BymlKeyedArrayChangelogBuilder("RailHash"),
            "CustomCullInfos" or "SpecialCullInfos" => new BymlKeyedArrayChangelogBuilder("ResourceName"),
            "Seats" => new BymlKeyedArrayChangelogBuilder("RidableType"),
            "VisibleSageOnNonMember" => new BymlKeyedArrayChangelogBuilder("SageType"),
            "SeriesArmorEffectList" => new BymlKeyedArrayChangelogBuilder("SeriesName"),
            "CropActorTable" => new BymlKeyedArrayChangelogBuilder("SrcActorName"),
            "SuspiciosBuffs" => new BymlKeyedArrayChangelogBuilder("SuspiciousBuffType"),
            "BoostOnlyTable" => new BymlKeyedArrayChangelogBuilder("Target"),
            "PartialConfigs" => new BymlKeyedArrayChangelogBuilder("TargetName"),
            "OverwritePropertiesEffect"
                or "OverwritePropertiesSound" => new BymlKeyedArrayChangelogBuilder("TargetTypeNameHash"),
            "BGParamArray" => new BymlKeyedArrayChangelogBuilder("TexName"),
            "TipsSetArray" => new BymlKeyedArrayChangelogBuilder("TipsType"),
            "TmbMesh" => new BymlKeyedArrayChangelogBuilder("TmbMeshPath"),
            "ActorPositionData" or "EventEntry" => new BymlKeyedArrayChangelogBuilder("$type"),
            "SB" or "T" or "U" => new BymlKeyedArrayChangelogBuilder("Umii"),
            "AlreadyReadInfo" => new BymlKeyedArrayChangelogBuilder("UpdateGameDataFlag"),
            "ConditionList" => new BymlKeyedArrayChangelogBuilder("WeaponEssence"),
            "WeaponTypeAndSubModelMapping" => new BymlKeyedArrayChangelogBuilder("WeaponType"),
            "Translate" or "Rotate" or "Scale" or "MarginNegative" or "MarginPositive" or "Rot" or "Trans"
                or "Pivot" or "PlayerPosOnClearEvent" or "EnokidaCameraPos"
                or "NearWoodStoragePos" => BymlDirectIndexArrayChangelogBuilder.Instance,
            "StaffRollSetArray" => BymlDirectIndexArrayChangelogBuilder.Instance,
            "ArmorCategory" or "ArmorDyeColor" or "ArmorSeries" or "BowEffect" or "FoodEffect"
                or "MaterialBowAttachmentTag" or "MaterialCategory" or "ShieldEffect" or "WeaponCategory"
                or "WeaponEffect" => info.Type switch {
                "game__ui__PouchSortTable" => BymlDirectIndexArrayChangelogBuilder.Instance,
                _ => BymlArrayChangelogBuilder.Instance
            },
            _ => info.Type switch {
                "game__component__ConditionParam" => BymlDirectIndexArrayChangelogBuilder.Instance,
                _ => BymlArrayChangelogBuilder.Instance
            }
        };
    }
}