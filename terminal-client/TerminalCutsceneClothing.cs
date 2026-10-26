using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalCutsceneClothing
{
	internal static bool TryBind(SkinnedMeshRenderer source, SkinnedMeshRenderer destination, Transform actorRoot, string logLabel, out Transform[] mapped, out Transform mappedRoot, out int auxiliaryBones, out string failure)
	{
		mapped = null;
		mappedRoot = null;
		auxiliaryBones = 0;
		failure = null;
		if (!source || !destination || !actorRoot)
		{
			failure = "source, destination, or actor root missing";
			return false;
		}
		Transform[] bones = source.bones;
		if (bones == null || bones.Length == 0)
		{
			failure = "source mesh has no bones";
			return false;
		}
		Dictionary<string, Transform> coreByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
		Transform[] bones2;
		if (destination.bones != null)
		{
			bones2 = destination.bones;
			foreach (Transform transform in bones2)
			{
				if ((bool)transform && !coreByName.ContainsKey(transform.name))
				{
					coreByName.Add(transform.name, transform);
				}
			}
		}
		if ((bool)destination.rootBone && !coreByName.ContainsKey(destination.rootBone.name))
		{
			coreByName.Add(destination.rootBone.name, destination.rootBone);
		}
		Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>(StringComparer.Ordinal);
		bones2 = actorRoot.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform transform2 in bones2)
		{
			if (!dictionary.ContainsKey(transform2.name))
			{
				dictionary.Add(transform2.name, transform2);
			}
		}
		Dictionary<Transform, Transform> sourceToActor = new Dictionary<Transform, Transform>();
		bones2 = bones;
		foreach (Transform transform3 in bones2)
		{
			if ((bool)transform3 && coreByName.TryGetValue(transform3.name, out var value))
			{
				sourceToActor[transform3] = value;
			}
		}
		Transform sourceRoot = source.rootBone;
		Transform actorClothingRoot = null;
		if ((bool)sourceRoot)
		{
			if (!sourceToActor.TryGetValue(sourceRoot, out actorClothingRoot))
			{
				if (!coreByName.TryGetValue(sourceRoot.name, out actorClothingRoot) && !dictionary.TryGetValue(sourceRoot.name, out actorClothingRoot))
				{
					failure = "root bone '" + sourceRoot.name + "' is absent from the animated actor";
					return false;
				}
				sourceToActor[sourceRoot] = actorClothingRoot;
			}
		}
		else
		{
			actorClothingRoot = (destination.rootBone ? destination.rootBone : actorRoot);
		}
		Dictionary<Transform, Transform> cloned = new Dictionary<Transform, Transform>();
		int clonedCount = 0;
		mapped = new Transform[bones.Length];
		List<string> list = new List<string>();
		for (int j = 0; j < bones.Length; j++)
		{
			Transform transform4 = bones[j];
			if (!transform4)
			{
				list.Add("#" + j);
				continue;
			}
			if (!sourceToActor.TryGetValue(transform4, out mapped[j]))
			{
				mapped[j] = EnsureAuxiliary(transform4);
			}
			if (!mapped[j])
			{
				list.Add(transform4.name);
			}
		}
		if (list.Count > 0)
		{
			foreach (Transform value3 in cloned.Values)
			{
				if ((bool)value3)
				{
					UnityEngine.Object.Destroy(value3.gameObject);
				}
			}
			mapped = null;
			failure = string.Format("{0} bone(s) could not be anchored ({1})", list.Count, string.Join(", ", list.GetRange(0, Math.Min(8, list.Count))) + ((list.Count > 8) ? "..." : ""));
			return false;
		}
		mappedRoot = actorClothingRoot;
		auxiliaryBones = clonedCount;
		Plugin.Log.LogDebug($"[{logLabel}] clothing bind remapped {bones.Length} weighted bone(s) and carried {auxiliaryBones} auxiliary transform(s)");
		return true;
		Transform EnsureAuxiliary(Transform sourceTransform)
		{
			if (!sourceTransform)
			{
				return null;
			}
			if (sourceToActor.TryGetValue(sourceTransform, out var value2))
			{
				return value2;
			}
			if (cloned.TryGetValue(sourceTransform, out value2))
			{
				return value2;
			}
			if (coreByName.TryGetValue(sourceTransform.name, out value2))
			{
				sourceToActor[sourceTransform] = value2;
				return value2;
			}
			Transform transform5;
			if (!sourceRoot || !sourceTransform.IsChildOf(sourceRoot))
			{
				transform5 = actorClothingRoot;
			}
			else
			{
				transform5 = ((sourceTransform.parent == sourceRoot) ? actorClothingRoot : EnsureAuxiliary(sourceTransform.parent));
			}
			if (!transform5)
			{
				return null;
			}
			Transform transform6 = new GameObject(sourceTransform.name + "__TerminalClothingBone")
			{
				layer = destination.gameObject.layer
			}.transform;
			transform6.SetParent(transform5, worldPositionStays: false);
			transform6.localPosition = sourceTransform.localPosition;
			transform6.localRotation = sourceTransform.localRotation;
			transform6.localScale = sourceTransform.localScale;
			cloned[sourceTransform] = transform6;
			sourceToActor[sourceTransform] = transform6;
			clonedCount++;
			return transform6;
		}
	}
}
