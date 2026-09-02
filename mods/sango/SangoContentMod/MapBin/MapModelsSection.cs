using System.Numerics;

namespace Sango.Content.MapBin;

/// <summary>
/// Port of the MapModels.OnLoad payload (IMapManageObject persisted fields): a placed scene model
/// identified by objId/objType/bindId/modelId with position, Euler rotation, and scale. modelId
/// indexes ModelConfig.json; bindId links the model to a gameplay object when &gt; 0.
/// </summary>
public sealed record MapModelObject
{
    public int ObjId { get; init; }
    public int ObjType { get; init; }
    public int BindId { get; init; }
    public int ModelId { get; init; }
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }
    public Vector3 Scale { get; init; }
}

/// <summary>Port of MapModels.OnLoad/OnSave: the count-prefixed list of placed map models.</summary>
public sealed record MapModelsSection
{
    public MapModelObject[] Objects { get; init; } = Array.Empty<MapModelObject>();

    public static MapModelsSection Read(int version, BinaryReader reader)
    {
        int count = reader.ReadInt32();
        var objects = new MapModelObject[count];
        for (int i = 0; i < count; i++)
        {
            int objId = reader.ReadInt32();
            int objType = reader.ReadInt32();
            int bindId = version >= 4 ? reader.ReadInt32() : 0;
            int modelId = reader.ReadInt32();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            objects[i] = new MapModelObject
            {
                ObjId = objId,
                ObjType = objType,
                BindId = bindId,
                ModelId = modelId,
                Position = position,
                Rotation = rotation,
                Scale = scale,
            };
        }
        return new MapModelsSection { Objects = objects };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Objects.Length);
        for (int i = 0; i < Objects.Length; i++)
        {
            MapModelObject obj = Objects[i];
            writer.Write(obj.ObjId);
            writer.Write(obj.ObjType);
            writer.Write(obj.BindId);
            writer.Write(obj.ModelId);
            writer.Write(obj.Position.X);
            writer.Write(obj.Position.Y);
            writer.Write(obj.Position.Z);
            writer.Write(obj.Rotation.X);
            writer.Write(obj.Rotation.Y);
            writer.Write(obj.Rotation.Z);
            writer.Write(obj.Scale.X);
            writer.Write(obj.Scale.Y);
            writer.Write(obj.Scale.Z);
        }
    }
}
