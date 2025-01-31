using AssetRipper.Core.Classes;
using AssetRipper.Core.Classes.Misc;
using AssetRipper.Core.Extensions;
using AssetRipper.Core.Interfaces;
using AssetRipper.Core.IO.Asset;
using AssetRipper.Core.Layout;
using AssetRipper.Core.Parser.Asset;
using AssetRipper.Core.Parser.Files.SerializedFiles.Parser;
using AssetRipper.Core.Structure;
using AssetRipper.Core.Structure.Assembly.Managers;
using System;
using System.Collections.Generic;

namespace AssetRipper.Core.Parser.Files.SerializedFiles
{
	/// <summary>
	/// A serialized file without any actual file backing it
	/// </summary>
	public class VirtualSerializedFile : ISerializedFile
	{
		public VirtualSerializedFile(AssetLayout layout)
		{
			Layout = layout;
		}

		public IUnityObjectBase GetAsset(long pathID)
		{
			IUnityObjectBase asset = FindAsset(pathID);
			if (asset == null)
			{
				throw new Exception($"Object with path ID {pathID} wasn't found");
			}
			return asset;
		}

		public IUnityObjectBase GetAsset(int fileIndex, long pathID)
		{
			if (fileIndex == VirtualFileIndex)
			{
				return GetAsset(pathID);
			}
			throw new NotSupportedException();
		}

		public IUnityObjectBase FindAsset(long pathID)
		{
			m_assets.TryGetValue(pathID, out IUnityObjectBase asset);
			return asset;
		}

		public IUnityObjectBase FindAsset(int fileIndex, long pathID)
		{
			if (fileIndex == VirtualFileIndex)
			{
				return FindAsset(pathID);
			}
			throw new NotSupportedException();
		}

		public IUnityObjectBase FindAsset(ClassIDType classID)
		{
			foreach (IUnityObjectBase asset in FetchAssets())
			{
				if (asset.ClassID == classID)
				{
					return asset;
				}
			}
			return null;
		}

		public IUnityObjectBase FindAsset(ClassIDType classID, string name)
		{
			foreach (IUnityObjectBase asset in FetchAssets())
			{
				if (asset.ClassID == classID && asset is INamedObject namedAsset)
				{
					if (namedAsset.ValidName == name)
					{
						return asset;
					}
				}
			}
			return null;
		}

		public ObjectInfo GetAssetEntry(long pathID)
		{
			throw new NotSupportedException();
		}

		public ClassIDType GetAssetType(long pathID)
		{
			return m_assets[pathID].ClassID;
		}

		public PPtr<T> CreatePPtr<T>(T asset) where T : IUnityObjectBase
		{
			if (asset.File == this)
			{
				return new PPtr<T>(VirtualFileIndex, asset.PathID);
			}
			throw new Exception($"Asset '{asset}' doesn't belong to {nameof(VirtualSerializedFile)}");
		}

		public IEnumerable<IUnityObjectBase> FetchAssets()
		{
			return m_assets.Values;
		}

		public T CreateAsset<T>(Func<AssetInfo, T> instantiator) where T : IUnityObjectBase
		{
			ClassIDType classID = typeof(T).ToClassIDType();
			AssetInfo assetInfo = new AssetInfo(this, ++m_nextId, classID);
			T instance = instantiator(assetInfo);
			m_assets.Add(instance.PathID, instance);
			return instance;
		}

		public string Name => nameof(VirtualSerializedFile);
		public Platform Platform => Layout.Info.Platform;
		public UnityVersion Version => Layout.Info.Version;
		public TransferInstructionFlags Flags => Layout.Info.Flags;

		public bool IsScene => throw new NotSupportedException();

		public AssetLayout Layout { get; }
		public IFileCollection Collection => throw new NotSupportedException();
		public IAssemblyManager AssemblyManager => throw new NotSupportedException();
		public IReadOnlyList<FileIdentifier> Dependencies => throw new NotSupportedException();

		public const int VirtualFileIndex = -1;

		private readonly Dictionary<long, IUnityObjectBase> m_assets = new Dictionary<long, IUnityObjectBase>();

		private long m_nextId;
	}
}
