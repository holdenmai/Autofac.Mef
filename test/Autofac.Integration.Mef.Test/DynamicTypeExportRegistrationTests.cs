﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Linq;
using System.Reflection;
using Autofac.Core.Registration;
using Autofac.Integration.Mef.Test.TestTypes;
using Xunit;

namespace Autofac.Integration.Mef.Test
{
    public class DynamicTypeExportRegistrationTests
    {
        [Fact]
        public void ImportManyFromAutofacExports()
        {
            var builder = new ContainerBuilder();
            var catalog = new TypeCatalog(typeof(ImportManyDependency));
            builder.RegisterComposablePartCatalog(catalog);
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                typeof(IAutofacDependency).IsAssignableFrom(type) && !type.IsInterface))
            {
                builder.RegisterType(type).Exported(x => x.As(typeof(IAutofacDependency)));
            }

            var container = builder.Build();
            var resolve = container.Resolve<ImportManyDependency>();
            Assert.Equal(2, resolve.Dependencies.Count());
        }

        [Fact]
        public void ImportWithMetadataFromAutofacExports()
        {
            var builder = new ContainerBuilder();
            var catalog = new TypeCatalog(typeof(ImportWithMetadataDependency));
            builder.RegisterComposablePartCatalog(catalog);
            const int metaInt = 10;
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                Attribute.GetCustomAttribute(type, typeof(ExportFromAutofacAttribute)) != null))
            {
                builder.RegisterType(type).Exported(x => x.As(type).WithMetadata("TheInt", metaInt));
            }

            var container = builder.Build();
            var resolve = container.Resolve<ImportWithMetadataDependency>();
            Assert.NotNull(resolve);
            Assert.NotNull(resolve.Dependency.Value);
            Assert.Equal(metaInt, resolve.Dependency.Metadata.TheInt);
        }

        [Fact]
        public void RestrictsExportsBasedOnValueType()
        {
            var builder = new ContainerBuilder();
            const string n = "name";
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                Attribute.GetCustomAttribute(type, typeof(ExportFromAutofacAttribute)) != null))
            {
                builder.RegisterType(type).Exported(x => x.AsNamed(type, n));
            }

            var container = builder.Build();
            var exports = container.ResolveExports<IAutofacDependency>(n);
            Assert.Empty(exports);
        }

        [Fact]
        public void DuplicateConstructorDependencyImportUsingAttribute()
        {
            var builder = new ContainerBuilder();
            var catalog = new TypeCatalog(typeof(ImportsDuplicateAutofacDependency));
            builder.RegisterComposablePartCatalog(catalog);
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                Attribute.GetCustomAttribute(type, typeof(ExportFromAutofacAttribute)) != null))
            {
                builder.RegisterType(type).Exported(x => x.As(type));
            }

            var container = builder.Build();
            var resolved = container.Resolve<ImportsDuplicateAutofacDependency>();
            Assert.NotNull(resolved.First);
            Assert.NotNull(resolved.Second);
        }

        [Fact]
        public void DuplicateConstructorDependencyImportUsingInterface()
        {
            var builder = new ContainerBuilder();
            var catalog = new TypeCatalog(typeof(ImportsDuplicateAutofacDependency));
            builder.RegisterComposablePartCatalog(catalog);
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                typeof(IAutofacDependency).IsAssignableFrom(type) && !type.IsInterface))
            {
                builder.RegisterType(type).Exported(x => x.As(type));
            }

            var container = builder.Build();
            var resolved = container.Resolve<ImportsDuplicateAutofacDependency>();
            Assert.NotNull(resolved.First);
            Assert.NotNull(resolved.Second);
        }

        [Fact]
        public void MixedDependencyConstructorDependencyImport()
        {
            var builder = new ContainerBuilder();
            var catalog = new TypeCatalog(typeof(ImportsMixedAutofacMefDependency), typeof(MefDependency));
            builder.RegisterComposablePartCatalog(catalog);
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(type =>
                Attribute.GetCustomAttribute(type, typeof(ExportFromAutofacAttribute)) != null))
            {
                builder.RegisterType(type).Exported(x => x.As(type));
            }

            var container = builder.Build();
            var resolved = container.Resolve<ImportsMixedAutofacMefDependency>();
            Assert.NotNull(resolved.First);
            Assert.NotNull(resolved.Second);
        }

        public interface IAutofacDependency
        {
        }

        [ExportFromAutofac]
        public class ExportFromAutofacDependencyA : IAutofacDependency
        {
        }

        [ExportFromAutofac]
        public class ExportFromAutofacDependencyB : IAutofacDependency
        {
        }

        public interface IDependency
        {
        }

        [Export(typeof(IDependency))]
        public class MefDependency : IDependency
        {
        }

        [Export]
        public class ImportManyDependency
        {
            [ImportMany]
            public IEnumerable<IAutofacDependency> Dependencies { get; set; }
        }

        [Export]
        public class ImportWithMetadataDependency
        {
            [Import]
            public Lazy<ExportFromAutofacDependencyB, IMetaWithDefault> Dependency { get; set; }
        }

        [Export]
        public class ImportsDuplicateAutofacDependency
        {
            public ExportFromAutofacDependencyA First { get; }

            public ExportFromAutofacDependencyB Second { get; }

            [ImportingConstructor]
            public ImportsDuplicateAutofacDependency(ExportFromAutofacDependencyA first, ExportFromAutofacDependencyB second)
            {
                First = first;
                Second = second;
            }
        }

        [Export]
        public class ImportsMixedAutofacMefDependency
        {
            public ExportFromAutofacDependencyA First { get; }

            public IDependency Second { get; }

            [ImportingConstructor]
            public ImportsMixedAutofacMefDependency(ExportFromAutofacDependencyA first, IDependency second)
            {
                First = first;
                Second = second;
            }
        }

        [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
        public sealed class ExportFromAutofacAttribute : Attribute
        {
        }
    }
}
