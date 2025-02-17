﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using log4net;
using CKAN.Versioning;

namespace CKAN.CmdLine
{
    public class Replace : ICommand
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Replace));

        public IUser User { get; set; }

        public Replace(CKAN.GameInstanceManager mgr, IUser user)
        {
            manager = mgr;
            User = user;
        }

        private GameInstanceManager manager;

        public int RunCommand(CKAN.GameInstance instance, object raw_options)
        {
            ReplaceOptions options = (ReplaceOptions) raw_options;

            if (options.ckan_file != null)
            {
                options.modules.Add(MainClass.LoadCkanFromFile(instance, options.ckan_file).identifier);
            }

            if (options.modules.Count == 0 && ! options.replace_all)
            {
                // What? No mods specified?
                User.RaiseMessage("{0}: ckan replace Mod [Mod2, ...]", Properties.Resources.Usage);
                User.RaiseMessage("  or   ckan replace --all");
                return Exit.BADOPT;
            }

            // Prepare options. Can these all be done in the new() somehow?
            var replace_ops = new RelationshipResolverOptions
                {
                    with_all_suggests  = options.with_all_suggests,
                    with_suggests      = options.with_suggests,
                    with_recommends    = !options.no_recommends,
                    allow_incompatible = options.allow_incompatible
                };

            var regMgr = RegistryManager.Instance(instance);
            var registry = regMgr.registry;
            var to_replace = new List<ModuleReplacement>();

            if (options.replace_all)
            {
                log.Debug("Running Replace all");
                var installed = new Dictionary<string, ModuleVersion>(registry.Installed());

                foreach (KeyValuePair<string, ModuleVersion> mod in installed)
                {
                    ModuleVersion current_version = mod.Value;

                    if ((current_version is ProvidesModuleVersion) || (current_version is UnmanagedModuleVersion))
                    {
                        continue;
                    }
                    else
                    {
                        try
                        {
                            log.DebugFormat("Testing {0} {1} for possible replacement", mod.Key, mod.Value);
                            // Check if replacement is available

                            ModuleReplacement replacement = registry.GetReplacement(mod.Key, instance.VersionCriteria());
                            if (replacement != null)
                            {
                                // Replaceable
                                log.InfoFormat("Replacement {0} {1} found for {2} {3}",
                                    replacement.ReplaceWith.identifier, replacement.ReplaceWith.version,
                                    replacement.ToReplace.identifier, replacement.ToReplace.version);
                                to_replace.Add(replacement);
                            }
                        }
                        catch (ModuleNotFoundKraken)
                        {
                            log.InfoFormat("{0} is installed, but it or its replacement is not in the registry",
                                mod.Key);
                        }
                    }
                }
            }
            else
            {
                foreach (string mod in options.modules)
                {
                    try
                    {
                        log.DebugFormat("Checking that {0} is installed", mod);
                        CkanModule modToReplace = registry.GetInstalledVersion(mod);
                        if (modToReplace != null)
                        {
                            log.DebugFormat("Testing {0} {1} for possible replacement", modToReplace.identifier, modToReplace.version);
                            try
                            {
                                // Check if replacement is available
                                ModuleReplacement replacement = registry.GetReplacement(modToReplace.identifier, instance.VersionCriteria());
                                if (replacement != null)
                                {
                                    // Replaceable
                                    log.InfoFormat("Replacement {0} {1} found for {2} {3}",
                                        replacement.ReplaceWith.identifier, replacement.ReplaceWith.version,
                                        replacement.ToReplace.identifier, replacement.ToReplace.version);
                                    to_replace.Add(replacement);
                                }
                                if (modToReplace.replaced_by != null)
                                {
                                    log.InfoFormat("Attempt to replace {0} failed, replacement {1} is not compatible",
                                        mod, modToReplace.replaced_by.name);
                                }
                                else
                                {
                                    log.InfoFormat("Mod {0} has no replacement defined for the current version {1}",
                                        modToReplace.identifier, modToReplace.version);
                                }
                            }
                            catch (ModuleNotFoundKraken)
                            {
                                log.InfoFormat("{0} is installed, but its replacement {1} is not in the registry",
                                    mod, modToReplace.replaced_by.name);
                            }
                        }
                    }
                    catch (ModuleNotFoundKraken kraken)
                    {
                        User.RaiseMessage(Properties.Resources.ReplaceModuleNotFound, kraken.module);
                    }
                }
            }
            if (to_replace.Count() != 0)
            {
                User.RaiseMessage("");
                User.RaiseMessage(Properties.Resources.Replacing);
                User.RaiseMessage("");
                foreach (ModuleReplacement r in to_replace)
                {
                    User.RaiseMessage(Properties.Resources.ReplaceFound,
                        r.ReplaceWith.identifier, r.ReplaceWith.version,
                        r.ToReplace.identifier, r.ToReplace.version);
                }

                bool ok = User.RaiseYesNoDialog(Properties.Resources.ReplaceContinuePrompt);

                if (!ok)
                {
                    User.RaiseMessage(Properties.Resources.ReplaceCancelled);
                    return Exit.ERROR;
                }

                // TODO: These instances all need to go.
                try
                {
                    HashSet<string> possibleConfigOnlyDirs = null;
                    new ModuleInstaller(instance, manager.Cache, User).Replace(to_replace, replace_ops, new NetAsyncModulesDownloader(User, manager.Cache), ref possibleConfigOnlyDirs, regMgr);
                    User.RaiseMessage("");
                }
                catch (DependencyNotSatisfiedKraken ex)
                {
                    User.RaiseMessage(Properties.Resources.ReplaceDependencyNotSatisfied,
                        ex.parent, ex.module, ex.version, instance.game.ShortName);
                }
            }
            else
            {
                User.RaiseMessage(Properties.Resources.ReplaceNotFound);
                return Exit.OK;
            }

            return Exit.OK;
        }
    }
}
