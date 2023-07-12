﻿using AssetManagement.MeshHandling;
using AssetManagement.Strategies.Fbx;
using CommonControls.Interfaces.AssetManagement;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using View3D;

namespace AssetManagement
{
    public class AssetManagement_DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<IAssetManagementFactory, AssetManagementFactory>();
            serviceCollection.AddTransient<IAssetImporter, FbxImportExport>();
        }
    }
}