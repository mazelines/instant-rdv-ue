// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;
using System.Collections.Generic;

public class InstantRdvSampleTarget : TargetRules
{
	public InstantRdvSampleTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V9;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal6_0;
		ExtraModuleNames.Add("InstantRdvSample");
	}
}
