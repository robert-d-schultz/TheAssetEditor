﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Xna.Framework;

namespace CommonControls.FileTypes.RigidModel.Transforms
{
    [Serializable]
    public struct RmvVector3
    {
        public float X;
        public float Y;
        public float Z;

        public bool IsAllZero()
        {
            if (X == 0 && Y == 0 && Z == 0)
                return true;
            return false;
        }

        public RmvVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public RmvVector3(Vector3 vector)
        {
            X = vector.X;
            Y = vector.Y;
            Z = vector.Z;
        }

        public override string ToString()
        {
            return $"{X}, {Y}, {Z}";
        }

        public Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }

        public Vector4 ToVector4(float w)
        {
            return new Vector4(X, Y, Z, w);
        }
    }
}
