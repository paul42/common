// Copyright 2019 Maintainers of NUKE.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Nuke.Common.Execution;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities.Collections;

namespace Nuke.Common.BuildServers
{

    [PublicAPI]
    public static class ArtifactExtensions
    {
        // private static readonly TargetDefinitionMetadata<int> PartitionKey =
        //     new TargetDefinitionMetadata<int>(nameof(PartitionKey));
        //
        // private static readonly TargetDefinitionMetadata<List<string>> ArtifactProductsKey =
        //     new TargetDefinitionMetadata<List<string>>(nameof(ArtifactProductsKey));
        //
        // private static readonly TargetDefinitionMetadata<LookupTable<Target, string>> ArtifactDependenciesKey =
        //     new TargetDefinitionMetadata<LookupTable<Target, string>>(nameof(ArtifactDependenciesKey));

        internal static readonly Dictionary<ITargetDefinition, int> Partitions =
            new Dictionary<ITargetDefinition, int>();

        internal static readonly LookupTable<ITargetDefinition, string> ArtifactProducts =
            new LookupTable<ITargetDefinition, string>();

        internal static readonly LookupTable<ITargetDefinition, (Target, string[])> ArtifactDependencies =
            new LookupTable<ITargetDefinition, (Target, string[])>();

        public static ITargetDefinition Produces(this ITargetDefinition targetDefinition, params string[] artifacts)
        {
            ArtifactProducts.AddRange(targetDefinition, artifacts);
            return targetDefinition;
        }

        public static ITargetDefinition Consumes(this ITargetDefinition targetDefinition, params Target[] targets)
        {
            targets.ForEach(x => targetDefinition.Consumes(x));
            return targetDefinition;
        }

        public static ITargetDefinition Consumes(this ITargetDefinition targetDefinition, Target target, params string[] artifacts)
        {
            ArtifactDependencies.Add(targetDefinition, (target, artifacts));
            return targetDefinition;
        }

        public static ITargetDefinition Partition(this ITargetDefinition targetDefinition, Expression<Func<Partition>> partition)
        {
            Partitions.Add(targetDefinition, partition.GetMemberInfo().GetCustomAttribute<PartitionAttribute>().Total);
            return targetDefinition;
        }
    }

    public class PartitionAttribute : ParameterAttribute
    {
        public PartitionAttribute(int total)
        {
            Total = total;
        }

        public int Total { get; }

        public override bool List => false;

        public override object GetValue(MemberInfo member, object instance)
        {
            var part = ParameterService.Instance.GetParameter<int?>(member);
            return part.HasValue
                ? new Partition { Part = part.Value, Total = Total }
                : Partition.Single;
        }
    }

    [TypeConverter(typeof(TypeConverter))]
    public class Partition
    {
        public static Partition Single { get; } = new Partition { Part = 1, Total = 1 };

        internal int Part { get; set; }
        internal int Total { get; set; }

        public class TypeConverter : System.ComponentModel.TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string stringValue)
                {
                    var values = stringValue.Split('/');
                    return new Partition
                           {
                               Part = int.Parse(values[0]),
                               Total = int.Parse(values[1])
                           };
                }

                return base.ConvertFrom(context, culture, value);
            }
        }

        public bool IsIn(int part)
        {
            return Total == 1 || part == Part;
        }

        public IEnumerable<T> GetCurrent<T>(IEnumerable<T> enumerable)
        {
            var i = 0;
            foreach (var item in enumerable)
            {
                if (i == Part - 1)
                    yield return item;
                i = (i + 1) % Total;
            }
        }

        public override string ToString()
        {
            return $"{Part}/{Total}";
        }
    }
}
