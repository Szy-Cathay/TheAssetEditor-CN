using System.IO;
using System.Text;

namespace Editors.VfxEditor;

internal static class TerryPreviewScene
{
    public static string Write(string directory)
    {
        var path = Path.Combine(directory, "preview.terry");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <project version="27" id="1ae000000000001">
              <pc type="QTU::ProjectPrefab"><data database="battle" is_skybox="0"/></pc>
              <pc type="QTU::Scene"><data version="42">
                <entity id="1ae000000000002" name="Default"><ECFileLayer export="true" bmd_export_type=""/></entity>
              </data></pc><pc type="QTU::Terrain"/>
            </project>
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "preview.1ae000000000002.layer"), $$"""
            <layer version="42"><entities>
              <entity id="1ae000000000003" name="AssetEditor VFX">
                <ECVFX vfx="{{TerryPreviewBuilder.EffectName}}" autoplay="true" scale="1" instance_name="asseteditor_preview"/>
                <ECVisibilitySettingsBattle visible_in_tactical_view="true" visible_in_tactical_view_only="false"/>
                <ECBattleProperties allow_in_outfield="false"/>
                <ECTransform position="512 0 512" rotation="0 0 0" scale="1 1 1" pivot="0 0 0"/>
                <ECTerrainClamp active="false" clamp_to_sea_level="false" terrain_oriented="false" fit_height_to_terrain="false"/>
              </entity></entities><associations><Logical/><Transform/></associations></layer>
            """, new UTF8Encoding(false));
        File.WriteAllText(path + ".user", """
            <?xml version="1.0" encoding="UTF-8"?>
            <project version="11">
              <load_config excluded_components="" tile_window="0,0,0,0" view_3d_needed="1"/>
              <settings><prefab_terrain_texture value="grass01"/><prefab_terrain_size_tiles value="8"/>
                <camera_type value="Perspective"/><rotation_mode value="Pan"/><drag_mode value="GroundPlane"/>
              </settings><terrain/><camera_2d position_x="0" position_y="0" zoom="-1"/>
              <camera_3d version="1"><camera_default version="42"><entity id="1ae000000000004">
                <ECCamera vertical_fov="60" aspect_ratio="1.5" near_clip="1" far_clip="12000"/>
                <ECTransform position="512 65 434.54" rotation="40 0 0" scale="1 1 1" pivot="0 0 0"/>
              </entity></camera_default><target_distance value="101"/><ortho_params value="0,0,100"/></camera_3d>
            </project>
            """, new UTF8Encoding(false));
        return path;
    }
}
