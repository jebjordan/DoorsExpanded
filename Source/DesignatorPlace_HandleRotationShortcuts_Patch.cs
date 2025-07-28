//#define PATCH_CALL_REGISTRY

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DoorsExpanded;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

[HarmonyPatch(typeof(Designator_Place))]
[HarmonyPatch("HandleRotationShortcuts")]
internal static class Designator_Place_HandleRotationShortcuts_Patch
{

    private static void DoorExpandedRotateAgainIfNeeded(Designator_Place designatorPlace, ref Rot4 placingRot,
              RotationDirection rotDirection)
    {
        // If placingRot is South and rotatesSouth is false, rotate again.
        if (placingRot == Rot4.South && designatorPlace.PlacingDef.GetDoorExpandedProps() is { rotatesSouth: false })
        {
            placingRot.Rotate(rotDirection);
        }
    }

    // Designator_Place.DoExtraGuiControls (internal lambda)
    // Designator_Place.HandleRotationShortcuts
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // This transforms the following code:
        //  designatorPlace.placingRot.Rotate(rotDirection);
        // into:
        //  designatorPlace.placingRot.Rotate(rotDirection);
        //  DoorExpandedRotateAgainIfNeeded(designatorPlace, ref designatorPlace.placingRot, rotDirection);

        var fieldof_Designator_Place_placingRot = AccessTools.Field(typeof(Designator_Place), "placingRot");
        var methodof_Rot4_Rotate = AccessTools.Method(typeof(Rot4), nameof(Rot4.Rotate));
        var methodof_RotateAgainIfNeeded =
            AccessTools.Method(typeof(Designator_Place_HandleRotationShortcuts_Patch), nameof(DoorExpandedRotateAgainIfNeeded));
        var instructionList = instructions.AsList();

        var searchIndex = 0;
        var placingRotFieldIndex = instructionList.FindIndex(
            instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
        while (placingRotFieldIndex >= 0)
        {
            searchIndex = placingRotFieldIndex + 1;
            var rotateIndex = instructionList.FindIndex(searchIndex,
                instr => instr.Calls(methodof_Rot4_Rotate));
            var nextPlacingRotFieldIndex = instructionList.FindIndex(searchIndex,
                instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
            if (rotateIndex >= 0 && (nextPlacingRotFieldIndex < 0 || rotateIndex < nextPlacingRotFieldIndex))
            {
                var replaceInstructions = new List<CodeInstruction>();
                // Need copy the Designator_Place instance on top of CIL stack 2 times, in reverse order
                // (due to stack popping):
                // (2) placingRot field access for the original Rotate call
                // (1) placingRot field access for 2nd arg to DoorExpandedRotateAgainIfNeeded call
                // (0) instance itself for 1st arg to DoorExpandedRotateAgainIfNeeded call
                replaceInstructions.AddRange(new[]
                {
                        new CodeInstruction(OpCodes.Dup),
                        new CodeInstruction(OpCodes.Dup),
                    });
                // Copy original instructions from placingRot field access to Rotate call (uses up (2)).
                var copiedRotateArgInstructions = instructionList.GetRange(placingRotFieldIndex,
                    rotateIndex - placingRotFieldIndex);
                replaceInstructions.AddRange(copiedRotateArgInstructions);
                replaceInstructions.Add(new CodeInstruction(OpCodes.Call, methodof_Rot4_Rotate));
                // Call DoorExpandedRotateAgainIfNeeded with required arguments.
                replaceInstructions.AddRange(copiedRotateArgInstructions); // uses up (1)
                replaceInstructions.Add(new CodeInstruction(OpCodes.Call, methodof_RotateAgainIfNeeded)); // uses up (0)
                instructionList.SafeReplaceRange(placingRotFieldIndex, rotateIndex - placingRotFieldIndex + 1,
                    replaceInstructions);
                searchIndex += replaceInstructions.Count - 1;
                nextPlacingRotFieldIndex = instructionList.FindIndex(searchIndex,
                    instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
            }
            placingRotFieldIndex = nextPlacingRotFieldIndex;
        }

        Debug.Log("[DE]: Patch Success?");

        return instructionList;
    }
}
