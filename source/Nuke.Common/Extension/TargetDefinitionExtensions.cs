// // Copyright 2019 Maintainers of NUKE.
// // Distributed under the MIT License.
// // https://github.com/nuke-build/nuke/blob/master/LICENSE
//
// using System;
// using System.Collections.Generic;
// using System.Linq;
//
// namespace Nuke.Common.Extension
// {
//     public static class TargetDefinitionExtensions
//     {
//         private static Dictionary<string, object> s_metadata;
//
//         internal static T Get<T>(this ITargetDefinition targetDefinition, TargetDefinitionMetadata<T> key)
//             where T : new()
//         {
//             return (T) (s_metadata[key.Key] = s_metadata.ContainsKey(key.Key) ? s_metadata[key.Key] : new T());
//         }
//
//         internal static void Put<T>(this ITargetDefinition targetDefinition, TargetDefinitionMetadata<T> key, T value)
//             where T : new()
//         {
//             s_metadata[key.Key] = value;
//         }
//     }
// }
