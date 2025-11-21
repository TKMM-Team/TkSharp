using TkSharp.Merging.Common.BinaryYaml;

namespace TkSharp.Merging.Mergers.BinaryYaml;

public sealed class BymlMergerKeyNameProvider : Singleton<BymlMergerKeyNameProvider>, IBymlMergerKeyNameProvider
{
    // ReSharper disable StringLiteralTypo
    public BymlKeyName GetKeyName(ReadOnlySpan<char> key, ReadOnlySpan<char> type, int depth)
    {
        return key switch {
            "Animal" or "Enemy" or "FallFloorInsect" or "Fish" or "GrassCut" or "Insect" or "NotDecayedLargeSwordList"
                or "NotDecayedSmallSwordList" or "NotDecayedSpearList" or "RainBonusMaterial" or "Seafood"
                or "SpObjCapsuleBlockMaster" or "Weapon" or "bow" or "bows" or "shields" or "weapons"
                or "helmets" => "name",
            "Actors" => type switch {
                "bcett" => "Hash",
                "game__component__ArmyManagerParam" => "ActorName",
                _ => null
            },
            "Table" => type switch {
                "game__colorvariation__ConversionActorNameToParasailPatternSetTable"
                    => "ParasailPattern",
                "game__ecosystem__DecayedWeaponMappingTable"
                    => "EquipmentDeathCountGmdHash",
                _ => null
            },
            "BoneInfoArray" => type switch {
                "game__component__DragonParam" => "Hash",
                "phive__LookIKResourceHeaderParam"
                    or "phive__TailBoneResourceHeaderParam" => "BoneName",
                _ => null
            },
            "Elements" => type switch {
                "game__enemy__DrakeSubModelInfo" => "BoneName",
                "game__gamebalance__LevelSensorTargetDefine" => ("ActorNameHash", "Plus"),
                _ => null
            },
            "Items" => type switch {
                "game__pouchcontent__EnhancementMaterial" => "Actor",
                _ => null
            },
            "List" => type switch {
                "game__sound__ShrineSpotBgmTypeInfoList" => "DungeonIndexStr",
                _ => null
            },
            "Contents" => type switch {
                "game__ui__FairyFountainGlobalSetting" => "Actor",
                _ => null
            },
            "SettingTable" => type switch {
                "game__ui__LargeDungeonFloorDefaultSettingTable" => "DungeonType",
                _ => null
            },
            "BrainVerbs" => "ActionSeqContainer",
            "ActionVerbContainerElements" => "ActionVerb",
            "ResidentActors" or "Settings"
                or "ShootableShareActorSettings" or "GoodsList" => "Actor",
            "BindActorInfo" => "ActorHolderKey",
            "RegisteredActorArray" or "RequirementList"
                or "Rewards" => "ActorName",
            "SharpInfoBowList" => "ActorNameHash",
            "PictureBookParamArray" => "ActorNameShort",
            "NavMeshObjects" => "Alias",
            "AliasEntityList" => "AliasEntity",
            "AliasSensorList" => "AliasSensor",
            "Anchors" => "AnchorName",
            "ArmorEffect" => "ArmorEffectType",
            "HornTypeAndAttachmentMapping" => "AttachmentName",
            "AttackParams" => "AttackType",
            "BlackboardParamBoolArray" or "BlackboardParamCustomTypeArray" or "BlackboardParamF32Array"
                or "BlackboardParamMtx33fArray" or "BlackboardParamMtx34fArray" or "BlackboardParamPtrArray"
                or "BlackboardParamQuatfArray" or "BlackboardParamS32Array" or "BlackboardParamS8Array"
                or "BlackboardParamStringArray" or "BlackboardParamU32Array" or "BlackboardParamU64Array"
                or "BlackboardParamU8Array" or "BlackboardParamVec3fArray"
                or "EditBBParams" => "BBKey",
            "DragonInfoList" => "BindPointRespawnGameDataHash",
            "OperationAngular" or "OperationLinear" => "Body",
            "BindBoneList" or "BoneList" or "BoneModifierSet" or "Bones" or "FruitOffsetTranslation"
                or "ModelBindSettings" or "StickWeaponBone" => "BoneName",
            "Categories" => "CallbackName",
            "CaveParams" => "CaveInstanceId",
            "CheckList" => "CheckType",
            "Object" => "ChemicalMaterial",
            "CharacterComponentPresetCollection" or "LayerHitMaskEntityCollection" or "LayerHitMaskSensorCollection"
                or "MaterialCollection" or "MaterialPresetCollection" or "MotionPropertiesCollection"
                or "PhysicsMaterialMappingInfoCollection" or "SubLayerHitMaskEntityCollection"
                or "SubLayerHitMaskSensorCollection"
                or "UserShapeTagMaskCollection" => "ComponentName",
            "HingeArray" or "RangeArray" => "ConstraintName",
            "CropYieldTable" => "CropName",
            "DungeonBossDifficultyGameData" => "DefeatedNumGameDataHash",
            "DoCondition" or "FinCondition" or "PickConditions"
                or "SuccessCondition" => "DefineNameHash",
            "FallenActorTable" => "DropActorName",
            "SmallDungeonLocationList" => "DungeonIndexStr",
            "VariationListForArmorDye" => "DyeColor",
            "HackEquip" => "EquipUserBbKey",
            "AdventureMemorySetArray"
                or "GlobalResidentEventList" => "EventName",
            "AutoPlayBoneVisibilities" or "AutoPlayMaterials" => "FileName",
            "WinningRateTable" => "FlintstonesNum",
            "ModelVariationAnims" => "Fmab",
            "OptionParam" => "FootIKMode",
            "PartialList" => "GameData",
            "PlacementGroups" => "GroupID",
            "EffectLimiterGroup" or "HiddenMaterialGroupList" => "GroupName",
            "Textures" => "guid",
            /* "AiGroups" or */ "Points" => "Hash",
            "Rails" => depth switch {
                0 => "Hash",
                _ => null
            },
            "HeadshotDamageParameters" => "HeadshotBoneName",
            "TransitionParam" => "Index",
            "OverwriteParam" => "InstanceId",
            "Interests" => "InterestType",
            "StrongInterests" => "InterestType",
            "ConditionArray" or "OverrideASEvReactVerbSettings" or "OverrideASEventReactSettings" or "SwitchParam"
                or "TriggerParams" or "Triggers" => "Key",
            "OverrideReactionVerbSettings" => "KeyActionVerb",
            "ShootableActorSettings" => ("KeyHash", "Actor"),
            "AttachmentGroupList" or "EnemyGroupList" => "Label",
            "ShopWeaponGroupList" or "WeaponGroupList" => ("Label", "EquipmentType"),
            "ActionSeqs" => "LabelHash",
            "CaveEntranceNormal" or "CaveEntranceSpecial" or "CaveEntranceWell" or "CheckPoint" or "City"
                or "District" or "DragonTears" or "Dungeon" or "Ground" or "ShopArmor" or "ShopDye" or "ShopGeneral"
                or "ShopInn" or "ShopJewelry" or "Shrine" or "SkyArchipelago" or "SpotBig" or "SpotBigArtifact"
                or "SpotBigMagma" or "SpotBigMountain" or "SpotBigOther" or "SpotBigTimber" or "SpotBigWater"
                or "SpotBigWithNameIcon" or "SpotMiddle" or "SpotMiddleArtifact" or "SpotMiddleMagma"
                or "SpotMiddleMountain" or "SpotMiddleOther" or "SpotMiddleTimber" or "SpotMiddleWater"
                or "SpotSmallArtifact" or "SpotSmallMagma" or "SpotSmallMountain" or "SpotSmallOther"
                or "SpotSmallTimber" or "SpotSmallWater" or "Stable" or "Tower"
                or "Underground" => type switch {
                    "locationarea" => "LocationName",
                    _ => null
                },
            "MiasmaAreaParam" => "MiasmaAreaType",
            "AnimationDrive" or "CColEntityNamePathAry" or "CColSensorNamePathAry" or "CheckPointSetting" or "Cloth"
                or "ClothAdvandecOption" or "ClothList" or "ClothReaction" or "CollectItem" or "CollidableList"
                or "ControllerEntityNamePathAry" or "ControllerSensorNamePathAry" or "ControllerSensorUnitAry"
                or "ExternalShapeNamePathAry" or "HelperBoneList" or "IntData" or "LookIKControllerNamePathAry"
                or "LookingControllerNamePathAry" or "MatterRigidBodyNamePathAry" or "MeshList" or "ParamTable"
                or "RagdollReaction" or "RagdollReactionList" or "RagdollStructure" or "Reaction"
                or "RigidBodyEntityNamePathAry" or "RigidBodySensorNamePathAry" or "ShapeList" or "ShapeNamePathAry"
                or "StringData" or "TailBoneControllerNamePathAry"
                or "Node" => "Name",
            "TowingHookParams" or "Property" => "NameHash",
            "ExtraNewsSourceInfo" or "TopNewsSourceInfo" => "NewsKeyName",
            "PictureBookPackInfoArray" => "PackActor",
            "VariationListForParasail" => "Pattern",
            "PhshMesh" => "PhshMeshPath",
            "PlgGdTable" => "PlgGuid",
            "PropertyDefinitions" or "Sources" => "PropertyNameHash",
            "Connections" => "RailHash",
            "CustomCullInfos" or "SpecialCullInfos" => "ResourceName",
            "Seats" => "RidableType",
            "VisibleSageOnNonMember" => "SageType",
            "SeriesArmorEffectList" => "SeriesName",
            "CropActorTable" => "SrcActorName",
            "SuspiciosBuffs" => "SuspiciousBuffType",
            "BoostOnlyTable" => "Target",
            "PartialConfigs" => "TargetName",
            "OverwritePropertiesEffect"
                or "OverwritePropertiesSound" => "TargetTypeNameHash",
            "BGParamArray" => "TexName",
            "TipsSetArray" => "TipsType",
            "TmbMesh" => "TmbMeshPath",
            "ActorPositionData" or "EventEntry" => "$type",
            "SB" or "T" or "U" => "Umii",
            "AlreadyReadInfo" => "UpdateGameDataFlag",
            "ConditionList" => "WeaponEssence",
            "WeaponTypeAndSubModelMapping" => "WeaponType",
            "SharpInfoList" or "SharpInfoBowList" or "SharpInfoShieldList" => "ActorNameHash",
            "DefaultValue" => type switch {
                // Workaround for selective DefaultValue arrays in GDL
                "Struct" => "Hash",
                _ => null
            },
            _ => null
        };
    }
}