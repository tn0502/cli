// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.ProjectModel.Files;
using NuGet.Frameworks;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Cli.Compiler.Common;

namespace Microsoft.DotNet.Tools.Build
{
    internal abstract class ProjectBuilder
    {
        private readonly bool _skipDependencies;

        public ProjectBuilder(bool skipDependencies)
        {
            _skipDependencies = skipDependencies;
        }

        private Dictionary<ProjectContextIdentity, CompilationResult> _compilationResults = new Dictionary<ProjectContextIdentity, CompilationResult>();

        public IEnumerable<CompilationResult> Build(IEnumerable<ProjectGraphNode> roots)
        {
            foreach (var projectNode in roots)
            {
                using (PerfTrace.Current.CaptureTiming($"{projectNode.ProjectContext.ProjectName()}"))
                {
                    yield return Build(projectNode);
                }
            }
        }

        public CompilationResult? GetCompilationResult(ProjectGraphNode projectNode)
        {
            CompilationResult result;
            if (_compilationResults.TryGetValue(projectNode.ProjectContext.Identity, out result))
            {
                return result;
            }
            return null;
        }

        protected virtual bool NeedsRebuilding(ProjectGraphNode projectNode)
        {
            return true;
        }

        protected virtual void ProjectSkiped(ProjectGraphNode projectNode)
        {
        }

        protected abstract void AfterCompile(ProjectGraphNode projectNode, CompilationResult result);

        protected abstract CompilationResult RunCompile(ProjectGraphNode projectNode);

        private CompilationResult Build(ProjectGraphNode projectNode)
        {
            CompilationResult result;
            if (_compilationResults.TryGetValue(projectNode.ProjectContext.Identity, out result))
            {
                return result;
            }
            result = CompileWithDependencies(projectNode);

            _compilationResults[projectNode.ProjectContext.Identity] = result;

            return result;
        }

        private CompilationResult CompileWithDependencies(ProjectGraphNode projectNode)
        {
            CompilationResult? result = null;

            if (!_skipDependencies)
            {
                foreach (var dependency in projectNode.Dependencies)
                {
                    var dependencyResult = Build(dependency);
                    if (dependencyResult == CompilationResult.Failure)
                    {
                        result =  CompilationResult.Failure;
                    }
                }
            }

            if (result == null)
            {
                var context = projectNode.ProjectContext;
                using (
                    PerfTrace.Current.CaptureTiming($"{projectNode.ProjectContext.ProjectName()}",
                        nameof(HasSourceFiles)))
                {
                    if (!HasSourceFiles(context))
                    {
                        result = CompilationResult.IncrementalSkip;
                    }
                }
            }

            if (result == null)
            {
                bool needsRebuilding;
                using (PerfTrace.Current.CaptureTiming($"{projectNode.ProjectContext.ProjectName()}", nameof(NeedsRebuilding)))
                {
                    needsRebuilding = NeedsRebuilding(projectNode);
                }

                if (needsRebuilding)
                {
                    using (PerfTrace.Current.CaptureTiming($"{projectNode.ProjectContext.ProjectName()}",nameof(RunCompile)))
                    {
                        result = RunCompile(projectNode);
                    }
                }
                else
                {
                    using (PerfTrace.Current.CaptureTiming($"{projectNode.ProjectContext.ProjectName()}", nameof(ProjectSkiped)))
                    {
                        ProjectSkiped(projectNode);
                    }
                    result = CompilationResult.IncrementalSkip;
                }
            }

            AfterCompile(projectNode, result.Value);
            return result.Value;
        }


        private static bool HasSourceFiles(ProjectContext context)
        {
            var compilerOptions = context.ProjectFile.GetCompilerOptions(context.TargetFramework, null);

            if (compilerOptions.CompileInclude == null)
            {
                return context.ProjectFile.Files.SourceFiles.Any();
            }

            var includeFiles = IncludeFilesResolver.GetIncludeFiles(compilerOptions.CompileInclude, "/", diagnostics: null);

            return includeFiles.Any();
        }
    }
}