﻿using AssetRipper.Core.Classes;
using AssetRipper.Core.Classes.AudioClip;
using AssetRipper.Core.Classes.Font;
using AssetRipper.Core.Classes.Material;
using AssetRipper.Core.Classes.Mesh;
using AssetRipper.Core.Classes.Shader;
using AssetRipper.Core.Classes.Sprite;
using AssetRipper.Core.Classes.TerrainData;
using AssetRipper.Core.Classes.Texture2D;
using AssetRipper.Core.Interfaces;
using AssetRipper.Core.Logging;
using AssetRipper.Core.Project.Exporters;
using AssetRipper.Core.Project.Exporters.Engine;
using AssetRipper.Core.Structure.GameStructure;
using AssetRipper.Core.Utils;
using AssetRipper.Core.VersionHandling;
using AssetRipper.Library.Attributes;
using AssetRipper.Library.Configuration;
using AssetRipper.Library.Exporters.Audio;
using AssetRipper.Library.Exporters.Meshes;
using AssetRipper.Library.Exporters.Miscellaneous;
using AssetRipper.Library.Exporters.Scripts;
using AssetRipper.Library.Exporters.Shaders;
using AssetRipper.Library.Exporters.Terrains;
using AssetRipper.Library.Exporters.Textures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AssetRipper.Library
{
	public class Ripper
	{
		static Ripper()
		{
			VersionManager.LegacyHandler = new LegacyHandler();
		}

		public Ripper() => LoadPlugins();

		public GameStructure GameStructure { get; private set; }
		/// <summary>
		/// Needs to be set before loading assets to ensure predictable behavior
		/// </summary>
		public LibraryConfiguration Settings { get; } = new();
		private bool ExportersInitialized { get; set; }

		public event Action OnStartLoadingGameStructure;
		public event Action OnFinishLoadingGameStructure;
		public event Action OnInitializingExporters;
		public event Action OnStartExporting;
		public event Action OnFinishExporting;

		private void LoadPlugins()
		{
			Logger.Info(LogCategory.Plugin, "Loading plugins...");
			string pluginsDirectory = ExecutingDirectory.Combine("Plugins");
			Directory.CreateDirectory(pluginsDirectory);
			var pluginTypes = new List<Type>();
			foreach(string dllFile in Directory.GetFiles(pluginsDirectory, "*.dll"))
			{
				try
				{
					Logger.Info(LogCategory.Plugin, $"Found assembly at {dllFile}");
					var assembly = Assembly.LoadFile(dllFile);
					foreach(var pluginAttr in assembly.GetCustomAttributes<RegisterPluginAttribute>())
					{
						pluginTypes.Add(pluginAttr.PluginType);
					}
				}
				catch(Exception ex)
				{
					Logger.Error(LogCategory.Plugin, $"Exception thrown while loading plugin assembly: {dllFile}", ex);
				}
			}
			foreach(var type in pluginTypes)
			{
				try
				{
					var plugin = (PluginBase)Activator.CreateInstance(type);
					plugin.CurrentRipper = this;
					plugin.Initialize();
					Logger.Info(LogCategory.Plugin, $"Initialized plugin: {plugin.Name}");
				}
				catch (Exception ex)
				{
					Logger.Error(LogCategory.Plugin, $"Exception thrown while initializing plugin: {type?.FullName ?? "<null>"}", ex);
				}
			}
			Logger.Info(LogCategory.Plugin, "Finished loading plugins.");
		}

		public GameStructure Load(IReadOnlyList<string> paths)
		{
			ResetData();
			if(paths.Count == 1)
				Logger.Info(LogCategory.General, $"Attempting to read files from {paths[0]}");
			else
				Logger.Info(LogCategory.General, $"Attempting to read files from {paths.Count} paths...");
			OnStartLoadingGameStructure?.Invoke();
			GameStructure = GameStructure.Load(paths, Settings);
			Logger.Info(LogCategory.General, "Finished reading files");
			OnFinishLoadingGameStructure?.Invoke();
			return GameStructure;
		}

		public IEnumerable<IUnityObjectBase> FetchLoadedAssets()
		{
			if (GameStructure == null) throw new NullReferenceException("GameStructure cannot be null");
			if (GameStructure.FileCollection == null) throw new NullReferenceException("FileCollection cannot be null");
			return GameStructure.FileCollection.FetchAssets();
		}

		public void ExportFile(string exportPath, IUnityObjectBase asset) => throw new NotImplementedException();
		public void ExportFile(string exportPath, IEnumerable<IUnityObjectBase> assets) => throw new NotImplementedException();

		public void ExportProject(string exportPath) => ExportProject(exportPath, new IUnityObjectBase[0]);
		public void ExportProject(string exportPath, IUnityObjectBase asset) => ExportProject(exportPath, new IUnityObjectBase[] { asset });
		public void ExportProject(string exportPath, IEnumerable<IUnityObjectBase> assets)
		{
			Logger.Info(LogCategory.Export, $"Attempting to export assets to {exportPath}...");
			List<IUnityObjectBase> list = new List<IUnityObjectBase>(assets ?? new IUnityObjectBase[0]);
			Settings.ExportPath = exportPath;
			Settings.Filter = list.Count == 0 ? LibraryConfiguration.DefaultFilter : GetFilter(list);
			InitializeExporters();
			Logger.Info(LogCategory.Export, "Starting pre-export");
			OnStartExporting?.Invoke();
			Logger.Info(LogCategory.Export, "Starting export");
			GameStructure.Export(Settings);
			Logger.Info(LogCategory.Export, "Finished exporting assets");
			OnFinishExporting?.Invoke();
			Logger.Info(LogCategory.Export, "Finished post-export");
		}

		public void ResetData()
		{
			ExportersInitialized = false;
			GameStructure?.Dispose();
			GameStructure = null;
		}

		public void ResetSettings() => Settings.ResetToDefaultValues();

		private static Func<IUnityObjectBase, bool> GetFilter(List<IUnityObjectBase> assets)
		{
			if (assets == null) throw new ArgumentNullException(nameof(assets));
			return assets.Contains;
		}

		private void InitializeExporters()
		{
			if (GameStructure == null) throw new NullReferenceException("GameStructure cannot be null");
			if (GameStructure.Exporter == null) throw new NullReferenceException("Project Exporter cannot be null");
			if (ExportersInitialized)
				return;

			OverrideNormalExporters();
			OnInitializingExporters?.Invoke();
			OverrideEngineExporters();

			ExportersInitialized = true;
		}

		private void OverrideNormalExporters()
		{
			//Miscellaneous exporters
			OverrideExporter<ITextAsset>(new TextAssetExporter(Settings));
			OverrideExporter<IFont>(new FontAssetExporter());
			OverrideExporter<IMovieTexture>(new MovieTextureAssetExporter());

			//Texture exporters
			TextureAssetExporter textureExporter = new TextureAssetExporter(Settings);
			OverrideExporter<ITexture2D>(textureExporter); //Texture2D and Cubemap
			OverrideExporter<ISprite>(textureExporter);

			//Shader exporters
			OverrideExporter<IShader>(new DummyShaderTextExporter());
			OverrideExporter<IShader>(new SimpleShaderExporter());

			//Audio exporters
			OverrideExporter<IAudioClip>(new NativeAudioExporter());
			OverrideExporter<IAudioClip>(new FmodAudioExporter(Settings));
			OverrideExporter<IAudioClip>(new AudioClipExporter(Settings));

			//Mesh exporters
			OverrideExporter<IMesh>(new GlbMeshExporter(Settings));
			OverrideExporter<IMesh>(new StlMeshExporter(Settings));
			OverrideExporter<IMesh>(new ObjMeshExporter(Settings));
			OverrideExporter<IMesh>(new UnifiedMeshExporter(Settings));

			//Terrain exporters
			OverrideExporter<ITerrainData>(new TerrainHeatmapExporter(Settings));
			OverrideExporter<ITerrainData>(new TerrainObjExporter(Settings));

			//Script exporters
			OverrideExporter<IMonoScript>(new ScriptExporter(GameStructure.FileCollection.AssemblyManager, Settings));
			OverrideExporter<IMonoScript>(new AssemblyDllExporter(GameStructure.FileCollection.AssemblyManager, Settings));
		}

		private void OverrideEngineExporters()
		{
			EngineAssetExporter engineExporter = new EngineAssetExporter(Settings);
			OverrideExporter<IMaterial>(engineExporter);
			OverrideExporter<ITexture2D>(engineExporter);
			OverrideExporter<IMesh>(engineExporter);
			OverrideExporter<IShader>(engineExporter);
			OverrideExporter<IFont>(engineExporter);
			OverrideExporter<ISprite>(engineExporter);
			OverrideExporter<IMonoBehaviour>(engineExporter);
		}

		private void OverrideExporter<T>(IAssetExporter exporter) => GameStructure.Exporter.OverrideExporter<T>(exporter, true);
		private void OverrideExporter<T>(IAssetExporter exporter, bool allowInheritance) => GameStructure.Exporter.OverrideExporter<T>(exporter, allowInheritance);
	}
}
