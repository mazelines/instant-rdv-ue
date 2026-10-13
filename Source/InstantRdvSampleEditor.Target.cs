// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;
using System.Collections.Generic;

public class InstantRdvSampleEditorTarget : TargetRules
{
	public InstantRdvSampleEditorTarget( TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V9;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal6_0;
		ExtraModuleNames.Add("InstantRdvSample");
	}
}
