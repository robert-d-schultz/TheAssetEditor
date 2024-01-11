﻿using System;
using System.Collections.Generic;
using CommonControls.Common;
using CommonControls.FileTypes.PackFiles.Models;
using CommunityToolkit.Diagnostics;
using Filetypes.ByteParsing;
using Serilog;
using static Filetypes.ByteParsing.ByteChunk;

namespace CommonControls.FileTypes.FastBin
{

    public class UndefinedSection
    { 
        public uint Version { get; init; }
        public string Name { get; init; }   
    }

    public class UndefinedSectionParser
    {
        public static UndefinedSection Parse(ByteChunk chunk, string sectionName, uint expectedVersion)
        {
            ushort itemCount = 0;
            var serialiseVersion = chunk.ReadUShort();
            if(serialiseVersion != 0)
                itemCount = chunk.ReadUShort();

            var unknownData = chunk.ReadUShort();   // Always 0?

            Guard.Equals(expectedVersion, serialiseVersion);
            Guard.Equals(itemCount, 0);

            return new UndefinedSection()
            {
                Name = sectionName,
                Version = serialiseVersion
            };
        }
    }

    public class FastBinFile2
    { 
        public UndefinedSection BATTLEFIELD_BUILDING_LIST { get; set; }
        public UndefinedSection BATTLEFIELD_BUILDING_LIST_FAR { get; set; }
        public UndefinedSection CAPTURE_LOCATION_SET { get; set; }
        public UndefinedSection EF_LINE_LIST { get; set; }
        public UndefinedSection GO_OUTLINES { get; set; }
        public UndefinedSection NON_TERRAIN_OUTLINES { get; set; }

        public UndefinedSection ZONES_TEMPLATE_LIST { get; set; }
        public UndefinedSection PREFAB_INSTANCE_LIST { get; set; }
        public UndefinedSection BMD_OUTLINE_LIST { get; set; }
        public UndefinedSection TERRAIN_OUTLINES { get; set; }
        public UndefinedSection LITE_BUILDING_OUTLINES { get; set; }
        public UndefinedSection CAMERA_ZONES { get; set; }
        public UndefinedSection CIVILIAN_DEPLOYMENT_LIST { get; set; }
        public UndefinedSection CIVILIAN_SHELTER_LIST { get; set; }
    }


    public class FastBinParser2
    {
        ILogger _logger = Logging.Create<FastBinParser>();

        public FasBinFile Load(PackFile pf)
        {
            _logger.Here().Information($"Parsing {pf.Name}____________");
            var outputFile = new FasBinFile();

            var chunk = pf.DataSource.ReadDataAsChunk();
            var formatStr = chunk.ReadFixedLength(8);

            if (formatStr != "FASTBIN0")
                throw new NotImplementedException("Unsupported file format for this parser");

            var rootVersion = chunk.ReadUShort();
            if (rootVersion != 27)
                throw new NotImplementedException("Unsupported version for this parser");

            var output = new FastBinFile2();
            output.BATTLEFIELD_BUILDING_LIST = UndefinedSectionParser.Parse(chunk, "BATTLEFIELD_BUILDING_LIST", 1);
            output.BATTLEFIELD_BUILDING_LIST_FAR = UndefinedSectionParser.Parse(chunk, "BATTLEFIELD_BUILDING_LIST_FAR", 1);
            output.CAPTURE_LOCATION_SET = UndefinedSectionParser.Parse(chunk, "CAPTURE_LOCATION_SET", 11);
            output.EF_LINE_LIST = UndefinedSectionParser.Parse(chunk, "EF_LINE_LIST", 0);
            output.GO_OUTLINES = UndefinedSectionParser.Parse(chunk, "GO_OUTLINES", 0);
            output.NON_TERRAIN_OUTLINES = UndefinedSectionParser.Parse(chunk, "NON_TERRAIN_OUTLINES", 1);
            output.ZONES_TEMPLATE_LIST = UndefinedSectionParser.Parse(chunk, "ZONES_TEMPLATE_LIST", 1);
            output.PREFAB_INSTANCE_LIST = UndefinedSectionParser.Parse(chunk, "PREFAB_INSTANCE_LIST", 1);
            output.BMD_OUTLINE_LIST = UndefinedSectionParser.Parse(chunk, "BMD_OUTLINE_LIST", 1);
            output.TERRAIN_OUTLINES = UndefinedSectionParser.Parse(chunk, "TERRAIN_OUTLINES", 1);
            output.LITE_BUILDING_OUTLINES = UndefinedSectionParser.Parse(chunk, "LITE_BUILDING_OUTLINES", 1);
            output.CAMERA_ZONES = UndefinedSectionParser.Parse(chunk, "CAMERA_ZONES", 0);
            output.CIVILIAN_DEPLOYMENT_LIST = UndefinedSectionParser.Parse(chunk, "CIVILIAN_DEPLOYMENT_LIST", 1);
            output.CIVILIAN_SHELTER_LIST = UndefinedSectionParser.Parse(chunk, "CIVILIAN_SHELTER_LIST", 1);

            //PARSE_BATTLEFIELD_BUILDING_LIST(outputFile, chunk);
            //PARSE_BATTLEFIELD_BUILDING_LIST_FAR(outputFile, chunk);
            //PARSE_CAPTURE_LOCATION_SET(outputFile, chunk);
            //PARSE_EF_LINE_LIST(outputFile, chunk);
            //PARSE_GO_OUTLINES(outputFile, chunk);   //5
            //PARSE_NON_TERRAIN_OUTLINES(outputFile, chunk);
            //PARSE_ZONES_TEMPLATE_LIST(outputFile, chunk);
            //PARSE_PREFAB_INSTANCE_LIST(outputFile, chunk);
            //PARSE_BMD_OUTLINE_LIST(outputFile, chunk);
            //PARSE_TERRAIN_OUTLINES(outputFile, chunk);
            //PARSE_LITE_BUILDING_OUTLINES(outputFile, chunk);
            //PARSE_CAMERA_ZONES(outputFile, chunk);
            //PARSE_CIVILIAN_DEPLOYMENT_LIST(outputFile, chunk);
            //PARSE_CIVILIAN_SHELTER_LIST(outputFile, chunk);
            PARSE_PROP_LIST(outputFile, chunk);

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
            GetVerionAndCount("PARSE_PROP_LIST", chunk, out var version, out var count);

            var byteOffsetStart = chunk.Index;

            chunk.Index = byteOffsetStart;
            if (version == 2)
            {
                //var keyIndex = chunk.ReadUShort(); // Could this be the key index?

                // if (keyIndex != 0)
                //     throw new NotImplementedException("Check what this is");

                var propFileRef = new string[count];
                for (int i = 0; i < count; i++)
                    propFileRef[i] = chunk.ReadString();

                var numChildren = chunk.ReadUInt32();

                ;



                for (int popChildIndex = 0; popChildIndex < numChildren; popChildIndex++)
                {
                    var start = chunk.Index;
                    // 103 bytes for each item from the looks of things - v15
                    // 102 bytes for each item from the looks of things - v14
                    var propVersion = chunk.ReadUShort();
                    if (!(propVersion == 15 || propVersion == 14))
                        throw new NotImplementedException("Check what this is");

                    var keyIndex = chunk.ReadInt32();


                    var matrix = new float[12];
                    for (int i = 0; i < 12; i++)
                        matrix[i] = chunk.ReadSingle();

                    var ind = chunk.Index;

                    // 8 bit flags..
                    //var dataTest0 = new List<UnknownParseResult>();
                    //var unkDataSize0 = 7;
                    //for (int i = 0; i < unkDataSize0; i++)
                    //{
                    //    dataTest0.Add(chunk.PeakUnknown());
                    //    chunk.ReadByte();
                    //}

                    var decal = chunk.ReadBool();
                    var logic_decal = chunk.ReadBool();
                    var is_fauna = chunk.ReadBool();
                    var snow_inside = chunk.ReadBool();
                    var snow_outside = chunk.ReadBool();
                    var destruction_inside = chunk.ReadBool();
                    var destruction_outside = chunk.ReadBool();
                    var animated = chunk.ReadBool();

                    //chunk.ReadByte();

                    var decal_parallax_scale = chunk.ReadSingle();
                    var decal_tiling = chunk.ReadSingle();



                    //var decal_override_gbuffer_normal = chunk.ReadBool();
                    //var visible_in_shroud = chunk.ReadBool();
                    //var decal_apply_to_terrain = chunk.ReadBool();
                    //var decal_apply_to_gbuffer_objects = chunk.ReadBool();
                    //var decal_render_above_snow = chunk.ReadBool();

                    var x0 = chunk.PeakUnknown();
                    var b0 = chunk.ReadByte();



                    //var unk = chunk.ReadUShort();  // Related to animated
                    //if (animated != 0)
                    //    throw new NotImplementedException("Check what this is");

                    var flags_serialise_version = chunk.ReadUShort();


                    // 5 bit flags
                    var dataTest1 = new List<UnknownParseResult>();
                    var unkDataSize1 = 11;
                    for (int i = 0; i < unkDataSize1; i++)
                    {
                        dataTest1.Add(chunk.PeakUnknown());
                        chunk.ReadByte();
                    }


                    var height_mode = chunk.ReadString();   // 254
                    var pdlc_mask = chunk.ReadInt32();
                    var cast_shadows = chunk.ReadBool();
                    var no_culling = chunk.ReadBool();

                    if (propVersion == 15)
                    {
                        var terrain_bent = chunk.ReadBool();
                    }

                    var dataRead = chunk.Index - start;

                    //var ukn = chunk.ReadBytes(3);
                }
                //170 start of item
                //254 start of bhm_parent
                //84 bytes
                //

                // 273 
            }
            else
            {
                throw new ArgumentException("Unsuported version");
            }
        }


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
