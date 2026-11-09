using KeySender.Models;

namespace KeySender;

/// <summary>
/// KeySender EAV property definitions. Source generator emits Get/Set / GetAsync helpers.
/// Cold fields use <see cref="EavCachePolicy.None"/> and are not preloaded.
/// </summary>
public static partial class KeySenderExtension
{
    [EavRelation(typeof(IKeySenderNodeFeature))]
    public static EavProperty<int?> BeforeDelayMsProperty { get; } =
        EavPropertyBuilder<int?>.Create()
            .WithId(KeySenderConst.BeforeDelayMsPropertyGuid)
            .WithName("BeforeDelayMs")
            .WithPolicy(EavCachePolicy.None)
            .WithDefaultValue(0)
            .WithStoreType(MioStoreType.Integer)
            .Build();

    [EavRelation(typeof(IKeySenderNodeFeature))]
    public static EavProperty<int?> AfterDelayMsProperty { get; } =
        EavPropertyBuilder<int?>.Create()
            .WithId(KeySenderConst.AfterDelayMsPropertyGuid)
            .WithName("AfterDelayMs")
            .WithPolicy(EavCachePolicy.None)
            .WithDefaultValue(0)
            .WithStoreType(MioStoreType.Integer)
            .Build();

    [EavRelation(typeof(IKeySenderNodeFeature))]
    public static EavProperty<KeyboardSequence?> SequenceProperty { get; } =
        EavPropertyBuilder<KeyboardSequence?>.Create()
            .WithId(KeySenderConst.SequencePropertyGuid)
            .WithName("Sequence")
            .WithPolicy(EavCachePolicy.None)
            .WithStoreType(MioStoreType.LongText)
            .WithPropertyConvert(KeyboardSequenceConvert.Instance)
            .Build();
}
