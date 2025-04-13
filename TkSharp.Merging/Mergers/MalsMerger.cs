using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using TkSharp.Core;
using TkSharp.Core.Models;

namespace TkSharp.Merging.Mergers;

public static class MalsMerger
{
    private const int LANG_EN = 0x6E0065; 
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<(TkChangelog Changelog, string MalsFile)> SelectMals(this IEnumerable<TkChangelog> changelogs, string locale)
    {
        return changelogs
            .Where(changelog => changelog.MalsFiles.Count > 0)
            .Select(changelog => (changelog, GetBestMals(changelog.MalsFiles, locale)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBestMals(List<string> targets, ReadOnlySpan<char> locale)
    {
        if (targets.Count == 1) {
            return targets[0];
        }

        if (locale.Length != 4) {
            throw new ArgumentException(
                "Invalid Mals locale, locale must be 4 characters.", nameof(locale));
        }

        ReadOnlySpan<byte> localeAsBytes = locale.Cast<char, byte>();
        int localeRegion = BitConverter.ToInt32(localeAsBytes[..4]);
        int localeLang = BitConverter.ToInt32(localeAsBytes[4..]);

        int level = 1;
        string? best = null;

        foreach (string target in targets) {
            if (target.Length < 9) {
                TkLog.Instance.LogWarning("Invalid mals file name: {FileName}", target);
                continue;
            }

            ReadOnlySpan<char> debug = target.AsSpan()[5..9];
            ReadOnlySpan<byte> targetLocale = target.AsSpan()[5..9]
                .Cast<char, byte>();
            
            int region = BitConverter.ToInt32(targetLocale[..4]);
            int lang = BitConverter.ToInt32(targetLocale[4..]);

            if (localeLang == lang && localeRegion == region) {
                return target;
            }

            if (localeLang == lang) {
                best = target;
                level = 2;
                continue;
            }

            if (level < 2 && lang == LANG_EN) {
                best = target;
            }
        }
        
        return best ?? targets[0];
    }
}