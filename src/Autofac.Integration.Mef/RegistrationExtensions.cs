﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;

namespace Autofac.Integration.Mef
{
    /// <summary>
    /// Extension methods that add MEF hosting capabilities to the container building classes.
    /// </summary>
    public static class RegistrationExtensions
    {
        /// <summary>
        /// Reference to the internal <see cref="System.Type"/> for <c>System.ComponentModel.Composition.ContractNameServices</c>,
        /// which is responsible for mapping types to MEF contract names.
        /// </summary>
        private static readonly Type ContractNameServices = typeof(ExportAttribute).Assembly.GetType("System.ComponentModel.Composition.ContractNameServices", true);

        /// <summary>
        /// Reference to the property <c>System.ComponentModel.Composition.ContractNameServices.TypeIdentityCache</c>,
        /// which holds the dictionary of <see cref="System.Type"/> to <see cref="string"/> contract name mappings.
        /// </summary>
        private static readonly PropertyInfo TypeIdentityCache = ContractNameServices.GetProperty("TypeIdentityCache", BindingFlags.GetProperty | BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// Expose the registered service to MEF parts as an export.
        /// </summary>
        /// <param name="registration">The component being registered.</param>
        /// <param name="configurationAction">Action on an object that configures the export.</param>
        /// <returns>A registration allowing registration to continue.</returns>
        public static IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> Exported<TLimit, TActivatorData, TSingleRegistrationStyle>(this IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> registration, Action<ExportConfigurationBuilder> configurationAction)
            where TSingleRegistrationStyle : SingleRegistrationStyle
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (configurationAction == null)
            {
                throw new ArgumentNullException(nameof(configurationAction));
            }

            var configuration = new ExportConfigurationBuilder();
            configurationAction(configuration);
            registration.OnRegistered(e => AttachExport(e.ComponentRegistryBuilder, e.ComponentRegistration, configuration));

            return registration;
        }

        /// <summary>
        /// Register a MEF catalog.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="catalog">The catalog to register.</param>
        /// <remarks>
        /// A simple heuristic/type scanning technique will be used to determine which MEF exports
        /// are exposed to other components in the Autofac container.
        /// </remarks>
        public static void RegisterComposablePartCatalog(this ContainerBuilder builder, ComposablePartCatalog catalog)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            RegisterComposablePartCatalog(builder, catalog, DefaultExposedServicesMapper);
        }

        /// <summary>
        /// Register a MEF catalog.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="catalog">The catalog to register.</param>
        /// <param name="interchangeServices">The services that will be exposed to other components in the container.</param>
        /// <remarks>
        /// Named and typed services only can be matched in the <paramref name="interchangeServices"/> collection.
        /// </remarks>
        public static void RegisterComposablePartCatalog(this ContainerBuilder builder, ComposablePartCatalog catalog, params Service[] interchangeServices)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (interchangeServices == null)
            {
                throw new ArgumentNullException(nameof(interchangeServices));
            }

            RegisterComposablePartCatalog(builder, catalog, ed =>
                interchangeServices
                    .OfType<TypedService>()
                    .Where(s => ed.ContractName == AttributedModelServices.GetContractName(s.ServiceType))
                    .Cast<Service>()
                    .Union(
                        interchangeServices
                            .OfType<KeyedService>()
                            .Where(s => ed.ContractName == (string)s.ServiceKey)));
        }

        /// <summary>
        /// Register a MEF catalog.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="catalog">The catalog to register.</param>
        /// <param name="exposedServicesMapper">A mapping function to transform ExportDefinitions into Services.</param>
        public static void RegisterComposablePartCatalog(this ContainerBuilder builder, ComposablePartCatalog catalog, Func<ExportDefinition, IEnumerable<Service>> exposedServicesMapper)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (exposedServicesMapper == null)
            {
                throw new ArgumentNullException(nameof(exposedServicesMapper));
            }

            builder.RegisterInstance(catalog).As(new UniqueService());

            foreach (var part in catalog.Parts)
            {
                RegisterComposablePartDefinition(builder, part, exposedServicesMapper);
            }
        }

        /// <summary>
        /// Register a MEF part definition.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="partDefinition">The part definition to register.</param>
        /// <param name="exposedServicesMapper">A mapping function to transform ExportDefinitions into Services.</param>
        public static void RegisterComposablePartDefinition(this ContainerBuilder builder, ComposablePartDefinition partDefinition, Func<ExportDefinition, IEnumerable<Service>> exposedServicesMapper)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (partDefinition == null)
            {
                throw new ArgumentNullException(nameof(partDefinition));
            }

            if (exposedServicesMapper == null)
            {
                throw new ArgumentNullException(nameof(exposedServicesMapper));
            }

            var partId = new UniqueService();
            var partRegistration = CreateBasePartRegistration(builder, partDefinition, partId);
            if (IsSharedInstance(partDefinition))
            {
                partRegistration.SingleInstance();
            }

            foreach (var iterExportDef in partDefinition.ExportDefinitions)
            {
                ProcessExportDefinition(builder, exposedServicesMapper, partId, iterExportDef);
            }
        }

        /// <summary>
        /// Register a MEF-attributed type as a component.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="partType">The attributed type to register.</param>
        /// <remarks>
        /// A simple heuristic/type scanning technique will be used to determine which MEF exports
        /// are exposed to other components in the Autofac container.
        /// </remarks>
        public static void RegisterComposablePartType(this ContainerBuilder builder, Type partType)
        {
            RegisterComposablePartType(builder, partType, DefaultExposedServicesMapper);
        }

        /// <summary>
        /// Register a MEF-attributed type as a component.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        /// <param name="partType">The attributed type to register.</param>
        /// <param name="exposedServicesMapper">A mapping function to transform ExportDefinitions into Services.</param>
        public static void RegisterComposablePartType(this ContainerBuilder builder, Type partType, Func<ExportDefinition, IEnumerable<Service>> exposedServicesMapper)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (partType == null)
            {
                throw new ArgumentNullException(nameof(partType));
            }

            if (exposedServicesMapper == null)
            {
                throw new ArgumentNullException(nameof(exposedServicesMapper));
            }

            RegisterComposablePartDefinition(
                builder,
                AttributedModelServices.CreatePartDefinition(partType, null, true),
                exposedServicesMapper);
        }

        /// <summary>
        /// Registers the <see cref="LazyWithMetadataRegistrationSource"/> and
        /// <see cref="StronglyTypedMetadataRegistrationSource"/> registration sources.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        public static void RegisterMetadataRegistrationSources(this ContainerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.RegisterSource(new LazyWithMetadataRegistrationSource());
            builder.RegisterSource(new StronglyTypedMetadataRegistrationSource());
        }

        /// <summary>
        /// Locate all of the MEF exports registered as supplying contract type T.
        /// </summary>
        /// <typeparam name="T">The contract type.</typeparam>
        /// <param name="context">The context to resolve exports from.</param>
        /// <returns>A list of exports.</returns>
        public static IEnumerable<Export> ResolveExports<T>(this IComponentContext context)
        {
            return context.ResolveExports<T>(AttributedModelServices.GetContractName(typeof(T)));
        }

        /// <summary>
        /// Locate all of the MEF exports registered as supplying contract type T.
        /// </summary>
        /// <param name="contractName">The contract name.</param>
        /// <param name="context">The context to resolve exports from.</param>
        /// <returns>A list of exports.</returns>
        public static IEnumerable<Export> ResolveExports<T>(this IComponentContext context, string contractName)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var service = new ContractBasedService(contractName, AttributedModelServices.GetTypeIdentity(typeof(T)));

            return context.ComponentRegistry
                .ServiceRegistrationsFor(service)
                .Select(cpt => context.ResolveComponent(new ResolveRequest(service, cpt, Enumerable.Empty<Parameter>())))
                .Cast<Export>();
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "The component registry is responsible for disposal of contained registrations.")]
        private static void AttachExport(IComponentRegistryBuilder registry, IComponentRegistration registration, ExportConfigurationBuilder exportConfiguration)
        {
            var contractService = new ContractBasedService(exportConfiguration.ContractName, exportConfiguration.ExportTypeIdentity);

            var rb = RegistrationBuilder.ForDelegate((c, p) =>
                {
                    var ctx = c.Resolve<IComponentContext>();
                    return new Export(
                        new ExportDefinition(exportConfiguration.ContractName, exportConfiguration.Metadata),
                        () => ctx.ResolveComponent(new ResolveRequest(contractService, new ServiceRegistration(ServicePipelines.DefaultServicePipeline, registration), Array.Empty<Parameter>())));
                })
                .As(contractService)
                .ExternallyOwned()
                .WithMetadata((IEnumerable<KeyValuePair<string, object>>)exportConfiguration.Metadata);

            registry.Register(rb.CreateRegistration());
        }

        private static IEnumerable<ServiceRegistration> ComponentsForContract(this IComponentContext context, ContractBasedImportDefinition definition, ContractBasedService contractService)
        {
            var componentsForContract = context
                .ComponentRegistry
                .ServiceRegistrationsFor(contractService)
                .Where(cpt =>
                    !definition.RequiredMetadata
                        .Except(cpt.Metadata.Select(m => new KeyValuePair<string, Type>(m.Key, m.Value.GetType())))
                        .Any())
                .ToList();

            if (definition.Cardinality == ImportCardinality.ExactlyOne && componentsForContract.Count == 0)
            {
                throw new ComponentNotRegisteredException(contractService);
            }

            return componentsForContract;
        }

        private static IRegistrationBuilder<ComposablePart, SimpleActivatorData, SingleRegistrationStyle> CreateBasePartRegistration(ContainerBuilder builder, ComposablePartDefinition partDefinition, UniqueService partId)
        {
            return builder.Register(c => partDefinition.CreatePart())
                    .OnActivating(e => SetPrerequisiteImports(e.Context, e.Instance))
                    .OnActivated(e => SetNonPrerequisiteImports(e.Context, e.Instance))
                    .As(partId);
        }

        private static IEnumerable<Service> DefaultExposedServicesMapper(ExportDefinition ed)
        {
            if (TryMapService(ed, out Service service))
            {
                yield return service;
            }
        }

        private static Type FindType(string exportTypeIdentity)
        {
            var cache = (Dictionary<Type, string>)TypeIdentityCache.GetValue(null, null);
            return cache.FirstOrDefault(kvp => kvp.Value == exportTypeIdentity).Key;
        }

        private static string GetTypeIdentity(ExportDefinition exportDef)
        {
            if (exportDef.Metadata.TryGetValue(CompositionConstants.ExportTypeIdentityMetadataName, out object typeIdentity))
            {
                return (string)typeIdentity;
            }

            return string.Empty;
        }

        private static bool IsSharedInstance(ComposablePartDefinition part)
        {
            return IsSharedInstance(part.Metadata);
        }

        private static bool IsSharedInstance(ExportDefinition export)
        {
            return IsSharedInstance(export.Metadata);
        }

        private static bool IsSharedInstance(IDictionary<string, object> metadata)
        {
            if (metadata != null)
            {
                if (metadata.TryGetValue(CompositionConstants.PartCreationPolicyMetadataName, out object pcp))
                {
                    // Here we use the MEF default of Shared, but using the Autofac default may make more sense.
                    if (pcp != null && (CreationPolicy)pcp == CreationPolicy.NonShared)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void ProcessExportDefinition(ContainerBuilder builder, Func<ExportDefinition, IEnumerable<Service>> exposedServicesMapper, UniqueService partId, ExportDefinition iterExportDef)
        {
            var exportDef = iterExportDef;
            var contractService = new ContractBasedService(exportDef.ContractName, GetTypeIdentity(exportDef));
            var exportIsShared = IsSharedInstance(exportDef);

            var exportId = new UniqueService();
            var exportReg = builder.Register(c =>
                {
                    var p = (ComposablePart)c.ResolveService(partId);
                    return new Export(exportDef, () => p.GetExportedValue(exportDef));
                })
                .As(exportId, contractService)
                .ExternallyOwned()
                .WithMetadata(exportDef.Metadata);

            // Issue #348: When a constructor takes in a duplicate dependency like:
            //   public ImportsDuplicateMefClass(ImportsMefDependency first, ImportsMefDependency second)
            // and each of those dependencies also take in the same thing:
            //   public ImportsMefDependency(IDependency dependency)
            // Then when the export/import process gets run, if the export doesn't have
            // the same lifetime scope sharing (per-instance vs. singleton) you
            // have trouble because the OnActivating from above in the original part
            // registration doesn't run, the chained-in prerequisite imports never get
            // populated, and everything fails. Setting the export registrations to be
            // the same lifetime scope as the part they correspond to fixes the issue.
            if (exportIsShared)
            {
                exportReg.SingleInstance();
            }

            var additionalServices = exposedServicesMapper(exportDef).ToArray();

            if (additionalServices.Length > 0)
            {
                var additionalRegistration = builder.Register(c => ((Export)c.ResolveService(exportId)).Value)
                            .As(additionalServices)
                            .ExternallyOwned()
                            .WithMetadata(exportDef.Metadata);

                if (exportIsShared)
                {
                    additionalRegistration.SingleInstance();
                }
            }
        }

        private static IEnumerable<Export> ResolveExports(this IComponentContext context, ContractBasedImportDefinition definition)
        {
            var contractService = new ContractBasedService(definition.ContractName, definition.RequiredTypeIdentity);

            var componentsForContract = context.ComponentsForContract(definition, contractService);

            var exportsForContract = componentsForContract
                .Select(cpt => context.ResolveComponent(new ResolveRequest(contractService, cpt, Enumerable.Empty<Parameter>())))
                .Cast<Export>()
                .ToList();

            return exportsForContract;
        }

        private static void SetImports(IComponentContext context, ComposablePart composablePart, bool prerequisite)
        {
            foreach (var import in composablePart
                .ImportDefinitions
                .Where(id => id.IsPrerequisite == prerequisite))
            {
                if (!(import is ContractBasedImportDefinition definition))
                {
                    throw new NotSupportedException(string.Format(CultureInfo.CurrentCulture, RegistrationExtensionsResources.ContractBasedOnly, import));
                }

                var exportsForContract = context.ResolveExports(definition);
                composablePart.SetImport(import, exportsForContract);
            }
        }

        private static void SetNonPrerequisiteImports(IComponentContext context, ComposablePart composablePart)
        {
            SetImports(context, composablePart, false);
            composablePart.Activate();
        }

        private static void SetPrerequisiteImports(IComponentContext context, ComposablePart composablePart)
        {
            SetImports(context, composablePart, true);
        }

        private static bool TryMapService(ExportDefinition ed, out Service service)
        {
            /* Issue 326: MEF is string based, not type-based, so when an export and
             * an import line up on contract (or type identity) it's always based on a
             * generated contract name rather than an actual type. Usually the contract
             * name and the type name are the same, just that the contract name is ONLY
             * the type name without any assembly qualifier. However, when you get to
             * generics, the type name gets mangled a bit. ITest<Foo> becomes ITest(Foo)
             * with parens instead of angle brackets. If you have multiple types with the
             * same name but in different assemblies, or nested types, things get even more
             * mangled. For this reason, you have to access the internal type-to-contract
             * map that MEF uses when building these items at runtime. Unfortunately, that's
             * not publicly exposed so we have to use reflection to reverse the lookup.
             *
             * Note we tried doing something like...
             * AppDomain.CurrentDomain.GetAssemblies()
             *     .SelectMany(asm => asm.GetTypes())
             *     .SingleOrDefault(t => AttributedModelServices.GetContractName(t) == exportTypeIdentity)
             *
             * But that doesn't work because when you enumerate the types in the assemblies
             * you only get OPEN generics, and many types expose services that are CLOSED
             * generics. That means the lambda above still won't find the service and things
             * still won't get registered. */
            var ct = FindType(ed.ContractName);
            if (ct != null)
            {
                service = new TypedService(ct);
                return true;
            }

            var et = FindType((string)ed.Metadata[CompositionConstants.ExportTypeIdentityMetadataName]);
            if (et != null)
            {
                service = new KeyedService(ed.ContractName, et);
                return true;
            }

            service = null;
            return false;
        }
    }
}
