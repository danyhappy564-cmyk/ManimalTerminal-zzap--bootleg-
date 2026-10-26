using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalMigrationBrainCompat
{
	private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly PropertyInfo ManagerInstance = AccessTools.Property(typeof(BrainManager), "Instance");

	private static readonly FieldInfo ActivatedBots = AccessTools.Field(typeof(BrainManager), "ActivatedBots");

	private static readonly FieldInfo ExcludedLayers = AccessTools.Field(typeof(BrainManager), "ExcludedLayers");

	internal static bool TryValidate(BotOwner bot, out string reason)
	{
		try
		{
			BuildCleanup(bot);
			reason = null;
			return true;
		}
		catch (Exception ex)
		{
			reason = "brain-compat:" + ex.GetType().Name;
			return false;
		}
	}

	internal static void BeforeRestart(BotOwner bot)
	{
		List<Action> list = BuildCleanup(bot);
		List<Exception> list2 = null;
		foreach (Action item2 in list)
		{
			try
			{
				item2();
			}
			catch (Exception item)
			{
				(list2 ?? (list2 = new List<Exception>())).Add(item);
			}
		}
		if (list2 != null)
		{
			throw new AggregateException("Brain migration cleanup was incomplete", list2);
		}
	}

	private static List<Action> BuildCleanup(BotOwner bot)
	{
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_0369: Unknown result type (might be due to invalid IL or missing references)
		//IL_038d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03da: Unknown result type (might be due to invalid IL or missing references)
		//IL_040c: Unknown result type (might be due to invalid IL or missing references)
		//IL_041d: Unknown result type (might be due to invalid IL or missing references)
		if (!bot || !bot.GetPlayer || bot.Brain?.BaseBrain == null)
		{
			throw new InvalidOperationException("Brain is not ready for migration");
		}
		if (ManagerInstance == null || ActivatedBots == null || ExcludedLayers == null)
		{
			throw new MissingMemberException("Installed BigBrain registry layout is unsupported");
		}
		object value = ManagerInstance.GetValue(null);
		IDictionary<IPlayer, BotOwner> activated = ActivatedBots.GetValue(value) as IDictionary<IPlayer, BotOwner>;
		IList excluded = ExcludedLayers.GetValue(value) as IList;
		if (activated == null || excluded == null)
		{
			throw new InvalidOperationException("Installed BigBrain registry is unavailable");
		}
		Player player = bot.GetPlayer;
		bool flag = activated.TryGetValue(player, out var value2);
		if (flag && (object)value2 != bot)
		{
			throw new InvalidOperationException("BigBrain player is registered to another bot");
		}
		BaseBrain baseBrain = bot.Brain.BaseBrain;
		List<Action> list = new List<Action>();
		AICoreLayer<BotLogicDecision>[] array = ValueEnumerableExtensions.ToArray<FromEnumerable<AICoreLayer<BotLogicDecision>>, AICoreLayer<BotLogicDecision>>(ValueEnumerable.AsValueEnumerable<AICoreLayer<BotLogicDecision>>((IEnumerable<AICoreLayer<BotLogicDecision>>)baseBrain._layers.Values));
		foreach (AICoreLayer<BotLogicDecision> wrapper in array)
		{
			if (wrapper == null)
			{
				continue;
			}
			Type type = wrapper.GetType();
			if (type.FullName != "DrakiaXYZ.BigBrain.Internal.CustomLayerWrapper")
			{
				continue;
			}
			object value3 = (AccessTools.Field(type, "customLayer") ?? throw new MissingFieldException(type.FullName, "customLayer")).GetValue(wrapper);
			if (value3 == null)
			{
				continue;
			}
			if (baseBrain.CurLayerInfo == wrapper)
			{
				MethodInfo stop = AccessTools.Method(type, "Stop", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "Stop");
				list.Add(() =>
				{
					try
					{
						stop.Invoke(wrapper, null);
					}
					catch (Exception ex)
					{
						Plugin.Log.LogWarning("[BotMigration] old custom layer Stop failed: " + (ex.InnerException ?? ex).GetType().Name);
					}
				});
			}
			Type type2 = value3.GetType();
			while (type2 != null && type2.FullName != "SAIN.Layers.SAINLayer")
			{
				type2 = type2.BaseType;
			}
			if (!(type2 == null))
			{
				MethodInfo method = type2.GetMethod("OnLayerChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMethodException(type2.FullName, "OnLayerChanged");
				AddEventRemoval(list, baseBrain, "OnLayerChangedTo", value3, method);
				object value4 = (type2.GetProperty("Bot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMemberException(type2.FullName, "Bot")).GetValue(value3);
				if (value4 != null)
				{
					object property = GetProperty(GetProperty(value4, "Decision"), "DecisionManager");
					MethodInfo method2 = type2.GetMethod("BotDecisionMade", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMethodException(type2.FullName, "BotDecisionMade");
					AddEventRemoval(list, property, "OnDecisionMade", value3, method2);
				}
			}
		}
		Type type3 = typeof(BrainManager).Assembly.GetType("DrakiaXYZ.BigBrain.Patches.BotStandartBotBrainActivatePatch", throwOnError: true);
		OnPlayerDeadOrUnspawn[] deathHandlers = ValueEnumerableExtensions.ToArray<Distinct<Where<WhereSelect<SelectMany<FromArray<Type>, Type, FieldInfo>, FieldInfo, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>(ValueEnumerableExtensions.Distinct<Where<WhereSelect<SelectMany<FromArray<Type>, Type, FieldInfo>, FieldInfo, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>(ValueEnumerableExtensions.Where<WhereSelect<SelectMany<FromArray<Type>, Type, FieldInfo>, FieldInfo, OnPlayerDeadOrUnspawn>, OnPlayerDeadOrUnspawn>(ValueEnumerableExtensions.Select<SelectMany<FromArray<Type>, Type, FieldInfo>, FieldInfo, OnPlayerDeadOrUnspawn>(ValueEnumerableExtensions.Where<SelectMany<FromArray<Type>, Type, FieldInfo>, FieldInfo>(ValueEnumerableExtensions.SelectMany<FromArray<Type>, Type, FieldInfo>(ValueEnumerable.AsValueEnumerable<Type>(type3.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)), (Func<Type, IEnumerable<FieldInfo>>)((Type type4) => type4.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))), (Func<FieldInfo, bool>)((FieldInfo field) => typeof(OnPlayerDeadOrUnspawn).IsAssignableFrom(field.FieldType))), (Func<FieldInfo, OnPlayerDeadOrUnspawn>)((FieldInfo field) => field.GetValue(null) as OnPlayerDeadOrUnspawn)), (Func<OnPlayerDeadOrUnspawn, bool>)((OnPlayerDeadOrUnspawn handler) => handler != null))));
		if (flag && deathHandlers.Length == 0)
		{
			throw new MissingMemberException("BigBrain activation cleanup delegate is unavailable");
		}
		object[] excludedForBot = ValueEnumerableExtensions.ToArray<FromEnumerable<object>, object>(ValueEnumerableExtensions.Where<FromEnumerable<object>, object>(ValueEnumerable.AsValueEnumerable<object>((IEnumerable<object>)excluded), (Func<object, bool>)((object entry) => entry != null && GetProperty(entry, "BotOwner") == bot)));
		list.Add(() =>
		{
			OnPlayerDeadOrUnspawn[] array2 = deathHandlers;
			foreach (OnPlayerDeadOrUnspawn value5 in array2)
			{
				player.OnPlayerDeadOrUnspawn -= value5;
			}
			object[] array3 = excludedForBot;
			foreach (object value6 in array3)
			{
				excluded.Remove(value6);
			}
			if (activated.TryGetValue(player, out var value7) && (object)value7 == bot)
			{
				activated.Remove(player);
			}
		});
		return list;
	}

	private static object GetProperty(object target, string name)
	{
		if (target == null)
		{
			throw new InvalidOperationException("Missing " + name + " owner");
		}
		return (AccessTools.Property(target.GetType(), name) ?? throw new MissingMemberException(target.GetType().FullName, name)).GetValue(target);
	}

	private static void AddEventRemoval(List<Action> cleanup, object publisher, string eventName, object target, MethodInfo method)
	{
		EventInfo eventInfo = publisher.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMemberException(publisher.GetType().FullName, eventName);
		Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, target, method);
		cleanup.Add(() =>
		{
			eventInfo.RemoveEventHandler(publisher, handler);
		});
	}
}
