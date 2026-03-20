using System;

namespace Theatre
{
    /// <summary>
    /// Tool groups for enabling/disabling categories of tools.
    /// The MCP server only announces tools whose group is enabled.
    /// </summary>
    [Flags]
    public enum ToolGroup
    {
        None             = 0,

        // Stage — GameObject
        StageGameObject  = 1 << 0,
        StageQuery       = 1 << 1,
        StageWatch       = 1 << 2,
        StageAction      = 1 << 3,
        StageRecording   = 1 << 4,

        // Stage — ECS
        ECSWorld         = 1 << 5,
        ECSEntity        = 1 << 6,
        ECSQuery         = 1 << 7,
        ECSAction        = 1 << 8,

        // Director
        DirectorScene    = 1 << 9,
        DirectorPrefab   = 1 << 10,
        DirectorAsset    = 1 << 11,
        DirectorAnim     = 1 << 12,
        DirectorSpatial  = 1 << 13,
        DirectorInput    = 1 << 14,
        DirectorConfig   = 1 << 15,

        // Presets
        StageAll         = StageGameObject | StageQuery | StageWatch
                         | StageAction | StageRecording,
        ECSAll           = ECSWorld | ECSEntity | ECSQuery | ECSAction,
        DirectorAll      = DirectorScene | DirectorPrefab | DirectorAsset
                         | DirectorAnim | DirectorSpatial | DirectorInput
                         | DirectorConfig,

        Everything       = StageAll | ECSAll | DirectorAll,
        GameObjectProject = StageAll | DirectorAll,
        ECSProject        = ECSAll | DirectorAll,
    }
}
