﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CommonControls.FileTypes.RigidModel.Types;
using Microsoft.Xna.Framework;

namespace CommonControls.FileTypes.RigidModel.MaterialHeaders
{
    public class TerrainTileMaterial : IMaterial
    {
        public VertexFormat BinaryVertexFormat { get; set; }
        public Vector3 PivotPoint { get; set; } = Vector3.Zero;
        public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
        public string ModelName { get; set; } = "TerrainTile";

        public string TerrainBaseStr { get; set; }
        public ModelMaterialEnum MaterialId { get; set; } = ModelMaterialEnum.TerrainTiles;
        public string TextureDirectory { get => ""; set { } }

        uint Unknown0 { get; set; }
        uint Unknown1 { get; set; }
        uint Unknown2 { get; set; }
        uint Unknown3 { get; set; }
        uint Unknown4 { get; set; }
        uint Unknown5 { get; set; }


        public IMaterial Clone()
        {
            return new TerrainTileMaterial()
            {
                MaterialId = MaterialId,
                TerrainBaseStr = TerrainBaseStr,
                Unknown0 = Unknown0,
                Unknown1 = Unknown1,
                Unknown2 = Unknown2,
                Unknown3 = Unknown3,
                Unknown4 = Unknown4,
                Unknown5 = Unknown5,
                AlphaMode = AlphaMode,
                BinaryVertexFormat = BinaryVertexFormat,
                ModelName = ModelName,
                PivotPoint = PivotPoint,

            };
        }

        public uint ComputeSize()
        {
            return (uint)ByteHelper.GetSize<TerrainTileStruct>();
        }

        public RmvTexture? GetTexture(TextureType texureType)
        {
            return null;
        }

        public List<RmvTexture> GetAllTextures()
        {
            return new List<RmvTexture>();
        }
        public void SetTexture(TextureType texureType, string path)
        {
        }

        public void UpdateEnumsBeforeSaving(UiVertexFormat uiVertexFormat, RmvVersionEnum outputVersion)
        {
            throw new NotImplementedException();
        }

        public void EnrichDataBeforeSaving(string[] boneNames, BoundingBox boundingBox)
        {
            throw new NotImplementedException();
        }
    }

    public class TerrainTileMaterialCreator : IMaterialCreator
    {
        public IMaterial Create(ModelMaterialEnum materialId, RmvVersionEnum rmvType, byte[] buffer, int offset)
        {
            var header = ByteHelper.ByteArrayToStructure<TerrainTileStruct>(buffer, offset);
            return new TerrainTileMaterial()
            {
                MaterialId = materialId,
                BinaryVertexFormat = VertexFormat.Position16_bit,
            };
        }

        public IMaterial CreateEmpty(ModelMaterialEnum materialId, RmvVersionEnum rmvType, VertexFormat vertexFormat)
        {
            return new TerrainTileMaterial();
        }

        public byte[] Save(IMaterial material)
        {
            throw new NotImplementedException();
        }
    }

#pragma warning disable CS0649 
    struct TerrainTileStruct
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Name;

        public uint Unknown0;
        public uint Unknown1;
        public uint Unknown2;
        public uint Unknown3;
        public uint Unknown4;
        public uint Unknown5;
    }
#pragma warning restore CS0649
}
