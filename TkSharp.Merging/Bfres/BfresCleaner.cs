using BfresLibrary;
using Microsoft.Extensions.Logging;
using TkSharp.Core;

namespace TkSharp.Merging.Bfres;

internal static class BfresCleaner
{
    public static bool RemoveUnusedMaterials(ResFile resFile)
    {
        var changed = false;

        foreach (var model in resFile.Models.Values) {
            if (model?.Materials is not { Count: > 0 }) {
                continue;
            }

            HashSet<int> usedIndices = [];
            foreach (var shape in model.Shapes.Values) {
                if (shape is not null) {
                    usedIndices.Add(shape.MaterialIndex);
                }
            }

            for (var i = model.Materials.Count - 1; i >= 0; i--) {
                if (usedIndices.Contains(i)) {
                    continue;
                }

                var materialName = model.Materials.GetKey(i);
                TkLog.Instance.LogInformation(
                    "The material '{MaterialName}' in model '{ModelName}' was not assigned to an object and will be removed.",
                    materialName,
                    model.Name);

                model.Materials.RemoveAt(i);

                foreach (var shape in model.Shapes.Values) {
                    if (shape is not null && shape.MaterialIndex > i) {
                        shape.MaterialIndex--;
                    }
                }

                changed = true;
            }
        }

        return changed;
    }
}
