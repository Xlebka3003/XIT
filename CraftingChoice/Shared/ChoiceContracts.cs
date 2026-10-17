using ProtoBuf;
using System.ComponentModel;
namespace XrbRecipeChoice;

[ProtoContract]
public sealed class ChoiceRequest
{
    [ProtoMember(4)] public int Protocol { get; set; }
    [ProtoMember(1)] public string Snapshot { get; set; } = "";
    [ProtoMember(2), DefaultValue(-1)] public int RecipeId { get; set; } = -1;
    [ProtoMember(3)] public int Revision { get; set; }
}
[ProtoContract]
public sealed class ChoiceOption
{
    [ProtoMember(1)] public int RecipeId { get; set; }
    [ProtoMember(2)] public byte[] Stack { get; set; } = Array.Empty<byte>();
    [ProtoMember(3)] public string RecipeKey { get; set; } = "";
}
[ProtoContract]
public sealed class ChoiceReply
{
    [ProtoMember(6)] public int Protocol { get; set; }
    [ProtoMember(1)] public string Snapshot { get; set; } = "";
    [ProtoMember(2)] public List<ChoiceOption> Options { get; set; } = new();
    [ProtoMember(3), DefaultValue(-1)] public int Selected { get; set; } = -1;
    [ProtoMember(4)] public int Revision { get; set; }
    [ProtoMember(5)] public bool Accepted { get; set; }
}
