// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using CommonControls.Common;
using CommonControls.FileTypes.PackFiles.Models;
using Filetypes.ByteParsing;
using Serilog;
using static Filetypes.ByteParsing.ByteChunk;

namespace CommonControls.FileTypes.FastBin
{
    public class FasBinFile
    { }


    public class FastBinParser
    {
        ILogger _logger = Logging.Create<FastBinParser>();

        public FasBinFile ParseFile(PackFile pf)
        {
            _logger.Here().Information($"Parsing {pf.Name}____________");
            var outputFile = new FasBinFile();

            var chunk = pf.DataSource.ReadDataAsChunk();
            var formatStr = chunk.ReadFixedLength(8);

            if (formatStr != "FASTBIN0")
                throw new NotImplementedException("Unsported fileformat for this parser");

            var rootVersion = chunk.ReadUShort();
            if (rootVersion != 23)
                throw new NotImplementedException("Unsported version for this parser");

            PARSE_BATTLEFIELD_BUILDING_LIST(outputFile, chunk);
            PARSE_BATTLEFIELD_BUILDING_LIST_FAR(outputFile, chunk);
            PARSE_CAPTURE_LOCATION_SET(outputFile, chunk);
            PARSE_EF_LINE_LIST(outputFile, chunk);
            PARSE_GO_OUTLINES(outputFile, chunk);   //5
            PARSE_NON_TERRAIN_OUTLINES(outputFile, chunk);
            PARSE_ZONES_TEMPLATE_LIST(outputFile, chunk);
            PARSE_PREFAB_INSTANCE_LIST(outputFile, chunk);
            PARSE_BMD_OUTLINE_LIST(outputFile, chunk);
            PARSE_TERRAIN_OUTLINES(outputFile, chunk);
            PARSE_LITE_BUILDING_OUTLINES(outputFile, chunk);
            PARSE_CAMERA_ZONES(outputFile, chunk);
            PARSE_CIVILIAN_DEPLOYMENT_LIST(outputFile, chunk);
            PARSE_CIVILIAN_SHELTER_LIST(outputFile, chunk);
            PARSE_PROP_LIST(outputFile, chunk);
            PARSE_PARTICLE_EMITTER_LIST(outputFile, chunk);
            PARSE_AI_HINTS(outputFile, chunk);
            PARSE_LIGHT_PROBE_LIST(outputFile, chunk);
            PARSE_TERRAIN_STENCIL_TRIANGLE_LIST(outputFile, chunk);
            PARSE_POINT_LIGHT_LIST(outputFile, chunk);
            PARSE_BUILDING_PROJECTILE_EMITTER_LIST(outputFile, chunk);
            PARSE_PLAYABLE_AREA(outputFile, chunk);
            PARSE_CUSTOM_MATERIAL_MESH_LIST(outputFile, chunk);
            PARSE_TERRAIN_STENCIL_BLEND_TRIANGLE_LIST(outputFile, chunk);
            PARSE_SPOT_LIGHT_LIST(outputFile, chunk);
            PARSE_SOUND_SHAPE_LIST(outputFile, chunk);
            PARSE_COMPOSITE_SCENE_LIST(outputFile, chunk);
            //PARSE_DEPLOYMENT_LIST(outputFile, chunk);
            //PARSE_BMD_CATCHMENT_AREA_LIST(outputFile, chunk);
            //PARSE_TOGGLEABLE_BUILDINGS_SLOT_LIST(outputFile, chunk);
            //PARSE_TERRAIN_DECAL_LIST(outputFile, chunk);
            //PARSE_TREE_LIST_REFERENCE_LIST(outputFile, chunk);
            //PARSE_GRASS_LIST_REFERENCE_LIST(outputFile, chunk);
            //PARSE_WATER_OUTLINES(outputFile, chunk);
            return null;
        }

        void PARSE_BATTLEFIELD_BUILDING_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_BATTLEFIELD_BUILDING_LIST", chunk, 1, 0);
        }

        void PARSE_BATTLEFIELD_BUILDING_LIST_FAR(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_BATTLEFIELD_BUILDING_LIST_FAR", chunk, 1, 0);
        }


        void PARSE_CAPTURE_LOCATION_SET(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_CAPTURE_LOCATION_SET", chunk, 2, 0);
        }


        void PARSE_EF_LINE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_EF_LINE_LIST", chunk, 0, 0);
        }


        void PARSE_GO_OUTLINES(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_GO_OUTLINES", chunk, 0, 0);
        }


        void PARSE_NON_TERRAIN_OUTLINES(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_NON_TERRAIN_OUTLINES", chunk, 1, 0);
        }

        void PARSE_ZONES_TEMPLATE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_ZONES_TEMPLATE_LIST", chunk, 1, 0);
        }

        void PARSE_PREFAB_INSTANCE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_PREFAB_INSTANCE_LIST", chunk, 1, 0);
        }

        void PARSE_BMD_OUTLINE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_BMD_OUTLINE_LIST", chunk, 0, 0);
        }

        void PARSE_TERRAIN_OUTLINES(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_TERRAIN_OUTLINES", chunk, 0, 1);
        }

        void PARSE_LITE_BUILDING_OUTLINES(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_LITE_BUILDING_OUTLINES", chunk, 0, 0);
        }

        void PARSE_CAMERA_ZONES(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_CAMERA_ZONES", chunk, 0, 0);
        }

        void PARSE_CIVILIAN_DEPLOYMENT_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_CAMERPARSE_CIVILIAN_DEPLOYMENT_LISTA_ZONES", chunk, 0, 0);
        }

        void PARSE_CIVILIAN_SHELTER_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            AssertVersionAndCount("PARSE_CIVILIAN_SHELTER_LIST", chunk, 0, 0);
        }

        void PARSE_PROP_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            var prop_list_serialise_version = chunk.ReadUShort();

            if (prop_list_serialise_version == 2)
            {
                var numPropkeys = chunk.ReadUInt32();
                var prop_keys = new string[numPropkeys];
                for (int i = 0; i < numPropkeys; i++)
                    prop_keys[i] = chunk.ReadString();

                var numProps = chunk.ReadUInt32();
                for (int j = 0; j < numProps; j++)
                {
                    var prop_serialise_version = chunk.ReadUShort();

                    var key_index = chunk.ReadInt32();
                    var key = prop_keys[key_index];

                    var transform_matrix = new float[12];
                    for (int k = 0; k < 12; k++)
                        transform_matrix[k] = chunk.ReadSingle();

                    var decal = chunk.ReadBool();
                    var logical_decal = chunk.ReadBool();
                    var is_fauna = chunk.ReadBool();
                    var snow_inside = chunk.ReadBool();
                    var snow_outside = chunk.ReadBool();
                    var destruction_inside = chunk.ReadBool();
                    var destruction_outside = chunk.ReadBool();
                    var animated = chunk.ReadBool();
                    var decal_parallax_scale = chunk.ReadSingle();
                    var decal_tiling = chunk.ReadSingle();
                    var decal_override_gbuffer_normal = chunk.ReadBool();

                    var flags_serialise_version = chunk.ReadUShort();
                    var allow_in_outfield = chunk.ReadBool();
                    var clamp_to_water_surface = chunk.ReadBool();
                    var spring = chunk.ReadBool();
                    var summer = chunk.ReadBool();
                    var autumn = chunk.ReadBool();
                    var winter = chunk.ReadBool();
                    if (flags_serialise_version >= 4)
                    {
                        var visible_in_tactical_view = chunk.ReadBool();
                        var visibile_in_tactical_view_only = chunk.ReadBool();
                    }

                    var visible_in_shroud = chunk.ReadBool();
                    var decal_apply_to_terrain = chunk.ReadBool();
                    var decal_apply_to_gbuffer_objects = chunk.ReadBool();
                    var decal_render_above_snow = chunk.ReadBool();
                    var height_mode = chunk.ReadString();

                    //Each of the bits in this Int64 correspond to a DLC, although it's not actually used in WH3
                    //The global_props.bin version of BMD has this as culture mask, so it might be that
                    var pdlc_mask = chunk.ReadInt64();

                    var cast_shadows = chunk.ReadBool();
                    if (prop_serialise_version > 21)
                    {
                        var no_culling = chunk.ReadBool();
                    }
                    var has_height_patch = chunk.ReadBool();
                    var apply_height_patch = chunk.ReadBool();
                    var include_in_fog = chunk.ReadBool();
                    var visible_without_shroud = chunk.ReadBool();
                    var use_dynamic_shadows = chunk.ReadBool();
                    var uses_terrain_vertex_offset = chunk.ReadBool();
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_PARTICLE_EMITTER_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            GetVerionAndCount("PARSE_PARTICLE_EMITTER_LIST", chunk, out var version, out var count);

            if (version == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var emitter_serialise_version = chunk.ReadUShort();

                    var key = chunk.ReadString();

                    var transform_matrix = new float[12];
                    for (int j = 0; j < 12; j++)
                        transform_matrix[j] = chunk.ReadSingle();

                    var emission_rate = chunk.ReadSingle();
                    var instance_name = chunk.ReadString();

                    var flags_serialise_version = chunk.ReadUShort();
                    var allow_in_outfield = chunk.ReadBool();
                    var clamp_to_water_surface = chunk.ReadBool();
                    var spring = chunk.ReadBool();
                    var summer = chunk.ReadBool();
                    var autumn = chunk.ReadBool();
                    var winter = chunk.ReadBool();
                    if (flags_serialise_version >= 4)
                    {
                        var visible_in_tactical_view = chunk.ReadBool();
                        var visibile_in_tactical_view_only = chunk.ReadBool();
                    }

                    var height_mode = chunk.ReadString();

                    //Again, this could instead be culture mask, each bit being a certain culture
                    var pdlc_mask = chunk.ReadInt64();

                    var autoplay = chunk.ReadBool();
                    var visible_in_shroud = chunk.ReadBool();
                    var parent_id = chunk.ReadInt32();
                    var visible_without_shroud = chunk.ReadBool();
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_AI_HINTS(FasBinFile outputFile, ByteChunk chunk)
        {
            //All this stuff should be blank for prefab BMDs, used in battle maps... probably
            var ai_hints_serialise_version = chunk.ReadUShort();

            var separators_serialise_version = chunk.ReadUShort();
            var numSeparators = chunk.ReadUShort();

            var directed_points_serialise_version = chunk.ReadUShort();
            var numDirectedPoints = chunk.ReadUShort();

            var polylines_serialise_version = chunk.ReadUShort();
            var numPolylines = chunk.ReadUShort();

            var polylines_list_serialise_version = chunk.ReadUShort();
            var numPolylinesAgain = chunk.ReadUShort();
        }

        void PARSE_LIGHT_PROBE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //Probably not used in prefab BMDs, but is present in the campaign global_props.bin version of BMD
            AssertVersionAndCount("PARSE_LIGHT_PROBE_LIST", chunk, 0, 0);

        }

        void PARSE_TERRAIN_STENCIL_TRIANGLE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //These are terrain hole triangles
            GetVerionAndCount("PARSE_TERRAIN_STENCIL_TRIANGLE_LIST", chunk, out var version, out var count);

            if (version == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var terrain_stencil_triangle_serialise_version = chunk.ReadUShort();

                    var vertices = new (float, float, float)[3];
                    for (int j = 0; j < 3; j++)
                        vertices[j] = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    var height_mode = chunk.ReadString();

                    if (terrain_stencil_triangle_serialise_version > 2)
                    {
                        var flags_serialise_version = chunk.ReadUShort();
                        var allow_in_outfield = chunk.ReadBool();
                        var clamp_to_water_surface = chunk.ReadBool();
                        var spring = chunk.ReadBool();
                        var summer = chunk.ReadBool();
                        var autumn = chunk.ReadBool();
                        var winter = chunk.ReadBool();
                        if (flags_serialise_version >= 4)
                        {
                            var visible_in_tactical_view = chunk.ReadBool();
                            var visibile_in_tactical_view_only = chunk.ReadBool();
                        }
                    }
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_POINT_LIGHT_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            GetVerionAndCount("PARSE_POINT_LIGHT_LIST", chunk, out var version, out var count);

            if (version == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var point_light_serialise_version = chunk.ReadUShort();

                    //xyz
                    var position = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    var radius = chunk.ReadSingle();

                    var rgb = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    var color_scale = chunk.ReadSingle();

                    var animation_type_enum = chunk.ReadByte();

                    var parameters = (chunk.ReadSingle(), chunk.ReadSingle());

                    var colour_min = chunk.ReadSingle();
                    var random_offet = chunk.ReadSingle();
                    var falloff_type = chunk.ReadString();
                    var lf_relative = chunk.ReadBool();
                    var height_mode = chunk.ReadString();
                    var light_probes_only = chunk.ReadBool();

                    var pdlc_mask = chunk.ReadInt64();

                    var flags_serialise_version = chunk.ReadUShort();
                    var allow_in_outfield = chunk.ReadBool();
                    var clamp_to_water_surface = chunk.ReadBool();
                    var spring = chunk.ReadBool();
                    var summer = chunk.ReadBool();
                    var autumn = chunk.ReadBool();
                    var winter = chunk.ReadBool();
                    if (flags_serialise_version >= 4)
                    {
                        var visible_in_tactical_view = chunk.ReadBool();
                        var visibile_in_tactical_view_only = chunk.ReadBool();
                    }
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_BUILDING_PROJECTILE_EMITTER_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //I think these are used in battle prefabs (wall towers)
            AssertVersionAndCount("PARSE_BUILDING_PROJECTILE_EMITTER_LIST", chunk, 0, 0);
        }

        void PARSE_PLAYABLE_AREA(FasBinFile outputFile, ByteChunk chunk)
        {
            //not really sure what this is, it's not an object placed in terry
            //so maybe just something set automatically
            var playable_area_serialise_version = chunk.ReadUShort();
            var has_been_set = chunk.ReadBool();
            if (playable_area_serialise_version == 3)
            {
                //area
                var min_x = chunk.ReadSingle(); //64;
                var min_y = chunk.ReadSingle(); //64;
                var max_x = chunk.ReadSingle(); //1920;
                var max_y = chunk.ReadSingle(); //1920;

                //valid location flags
                var valid_location_flags_serialise_version = chunk.ReadUShort();
                var valid_north = chunk.ReadBool(); //true
                var valid_south = chunk.ReadBool(); //true
                var valid_east = chunk.ReadBool(); //true
                var valid_west = chunk.ReadBool(); //true
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_CUSTOM_MATERIAL_MESH_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //Polymesh
            GetVerionAndCount("PARSE_CUSTOM_MATERIAL_MESH_LIST", chunk, out var version, out var count);

            if (version == 4)
            {
                for (int i = 0; i < count; i++)
                {
                    var custom_material_mesh_serialise_version = chunk.ReadUShort();

                    var num_vertices = chunk.ReadInt32();
                    var vertices = new (float, float, float)[num_vertices];
                    for (int j = 0; j < num_vertices; j++)
                        vertices[j] = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    //I believe these represent the triangulation of the polymesh
                    //Except I don't think it really matters since the polymesh is always planar...?
                    var num_indices = chunk.ReadInt32();
                    var indices = new ushort[num_vertices];
                    for (int k = 0; k < num_indices; k++)
                        indices[k] = chunk.ReadUShort();

                    var material = chunk.ReadString();

                    var height_mode = chunk.ReadString();

                    var flags_serialise_version = chunk.ReadUShort();
                    var allow_in_outfield = chunk.ReadBool();
                    var clamp_to_water_surface = chunk.ReadBool();
                    var spring = chunk.ReadBool();
                    var summer = chunk.ReadBool();
                    var autumn = chunk.ReadBool();
                    var winter = chunk.ReadBool();
                    if (flags_serialise_version >= 4)
                    {
                        var visible_in_tactical_view = chunk.ReadBool();
                        var visibile_in_tactical_view_only = chunk.ReadBool();
                    }

                    if (custom_material_mesh_serialise_version > 4)
                    {
                        var transform_matrix = new float[12];
                        for (int n = 0; n < 12; n++)
                            transform_matrix[n] = chunk.ReadSingle();

                        var snow_inside = chunk.ReadBool();
                        var snow_outside = chunk.ReadBool();
                        var destruction_inside = chunk.ReadBool();
                        var destruction_outside = chunk.ReadBool();
                        var visible_in_shroud = chunk.ReadBool();
                        var visible_without_shroud = chunk.ReadBool();
                    }
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_TERRAIN_STENCIL_BLEND_TRIANGLE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //No idea what this is.
            //It's NOT related to PARSE_TERRAIN_STENCIL_TRIANGLE_LIST, at least not for prefabs
            AssertVersionAndCount("PARSE_TERRAIN_STENCIL_BLEND_TRIANGLE_LIST", chunk, 0, 0);
        }

        void PARSE_SPOT_LIGHT_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            GetVerionAndCount("PARSE_SPOT_LIGHT_LIST", chunk, out var version, out var count);

            if (version == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var length = chunk.ReadSingle();
                    var inner_angle = chunk.ReadSingle();
                    var outer_angle = chunk.ReadSingle();
                    var falloff = chunk.ReadSingle();
                    var gobo = chunk.ReadString();
                    var volumetric = chunk.ReadBool();

                    var pdlc_mask = chunk.ReadInt64();

                    //x,y,z
                    var position = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    //i,j,k,w
                    var end = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    var rgb = (chunk.ReadSingle(), chunk.ReadSingle(), chunk.ReadSingle());

                    var flags_serialise_version = chunk.ReadUShort();
                    var allow_in_outfield = chunk.ReadBool();
                    var clamp_to_water_surface = chunk.ReadBool();
                    var spring = chunk.ReadBool();
                    var summer = chunk.ReadBool();
                    var autumn = chunk.ReadBool();
                    var winter = chunk.ReadBool();
                    if (flags_serialise_version >= 4)
                    {
                        var visible_in_tactical_view = chunk.ReadBool();
                        var visibile_in_tactical_view_only = chunk.ReadBool();
                    }

                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        void PARSE_SOUND_SHAPE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            //Maybe used in prefab BMDs, will eventually need to fill this out
            AssertVersionAndCount("PARSE_SOUND_SHAPE_LIST", chunk, 0, 0);
        }

        void PARSE_COMPOSITE_SCENE_LIST(FasBinFile outputFile, ByteChunk chunk)
        {
            GetVerionAndCount("PARSE_COMPOSITE_SCENE_LIST", chunk, out var version, out var count);

            if (version == 1)
            {
                for (int i = 0; i < count; i++)
                {
                    var composite_scene_reference_serialise_version = chunk.ReadUShort();

                    var transform_matrix = new float[12];
                    for (int j = 0; j < 12; j++)
                        transform_matrix[j] = chunk.ReadSingle();

                    var scene_file = chunk.ReadString();

                    var height_mode = chunk.ReadString();

                    //Culture mask maybe, etc.
                    var pdlc_mask = chunk.ReadInt64();

                    var autoplay = chunk.ReadBool();
                    var visible_in_shroud = chunk.ReadBool();
                    var no_culling = chunk.ReadBool();
                    var script_id = chunk.ReadString();
                    var parent_script_id = chunk.ReadString();
                    var visible_without_shroud = chunk.ReadBool();
                    var visible_in_tactical_view = chunk.ReadBool();
                    var visible_in_tactical_view_only = chunk.ReadBool();
                }
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }

        //I really don't think the rest of this stuff is used in prefabs, so skip for now:
        //DEPLOYMENT_LIST
        //BMD_CATCHMENT_AREA_LIST
        //TOGGLEABLE_BUILDINGS_SLOT_LIST
        //TERRAIN_DECAL_LIST
        //TREE_LIST_REFERENCE_LIST
        //GRASS_LIST_REFERENCE_LIST
        //WATER_OUTLINES

        void GetVerionAndCount(string desc, ByteChunk chunk, out ushort serialiseVersion, out int itemCount)
        {
            var indexAtStart = chunk.Index;

            itemCount = -1;
            serialiseVersion = chunk.ReadUShort();
            if (serialiseVersion != 0)
                itemCount = chunk.ReadUShort();
            var unknownData = chunk.ReadUShort();   // Always 0?

            _logger.Here().Information($"At index {indexAtStart} - Version:{serialiseVersion} NumElements:{itemCount} unk:{unknownData} - {desc}");

        }

        void AssertVersionAndCount(string desc, ByteChunk chunk, ushort expectedSerialiseVersion, uint expectedItemCount)
        {
            GetVerionAndCount(desc, chunk, out var acutalSerialiseVersion, out var actualItemCount);

            //if (acutalSerialiseVersion != expectedSerialiseVersion)
            //    throw new ArgumentException("Unexpected version");
            //
            //if (actualItemCount != expectedItemCount)
            //    throw new ArgumentException("Unexpected item count");
        }

    }
}
