This file contains the results of mod pack tests that were done on 8/21/26.


These tests check how the current system handles different mod packs. Each test uses a different mod pack.


Each test entry gives the mod pack used, the result, the fail message if the test failed, and the console output from the console tab in the manager app if the test failed.

For all tests, I also selected to wipe the world.

Tests were conducted using the "Change pack" feature in the manager app rather than doing full deployments from scratch every time.


### Test 1
- Mod Pack: `custom-forge-1.20.1-MilesPack.zip`
- Pre-change Summary:
```
This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the server. If the server fails to start, check this skipped list first. Examples: embeddium-0.3.31+mc1.20.1.jar, entity_model_features-3.2.4-1.20.1-forge.jar, entityculling-forge-1.10.5-mc1.20.1.jar, mob_grinding_utils-1.20.1-1.1.0.jar, notenoughanimations-forge-1.12.4-mc1.20.1.jar, oculus-mc1.20.1-1.8.0.jar (and 1 more).

Some jar files in this zip do not declare whether they are client-only or server-side. Setup will keep them on the server after the exclude list and in-jar client strips. If the server fails to start, check those jars first. Examples: aether-1.20.1-1.5.2-neoforge.jar, appliedenergistics2-forge-15.4.10.jar, betterdays-1.20.1-3.3.4.5-FORGE.jar, BiomesOPlenty-forge-1.20.1-19.0.0.96.jar, born_in_chaos_[Forge]1.20.1_1.7.4.jar, chunkloaders-1.2.9-forge-mc1.20.1.jar (and 61 more).

Pack: custom-forge-1.20.1-MilesPack
Kind: UnstructuredServer
Minecraft: 1.20.1
Loader: forge
Required Java: 17
Root jars install into mods/.
Files in zip: 78
  Server-side jars: 70
  Client-only (not installed on the server): 7
    In-jar metadata: 0
    Override list: 7
  No side metadata (kept): 67
Override-list skipped jars:
  embeddium-0.3.31+mc1.20.1.jar
  entity_model_features-3.2.4-1.20.1-forge.jar
  entityculling-forge-1.10.5-mc1.20.1.jar
  mob_grinding_utils-1.20.1-1.1.0.jar
  notenoughanimations-forge-1.12.4-mc1.20.1.jar
  oculus-mc1.20.1-1.8.0.jar
  sound-physics-remastered-forge-1.20.1-1.4.10.jar
Warnings:
  7 jar(s) skipped by the CurseForge exclude list (known client-only).
  67 jar(s) have no in-jar side metadata; kept (server pack assumed). This is not a Modrinth env.server strip.
  Archive has jars at the root (no mods/ folder); they will install into mods/.
```
- Result: `FAIL. The setup exceeded the 12 RCON connection attempts.`
- Fail Message: `Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.`
- Console Output: 
```
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Consumed 33.892s CPU time.
minecraft.service: Scheduled restart job, restart counter is at 9.
Stopped Minecraft server.
minecraft.service: Consumed 33.892s CPU time.
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
2026-08-21 21:44:50,987 main WARN Advanced terminal features are not available in this environment
[21:44:51] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher running: args [--launchTarget, forgeserver, --fml.forgeVersion, 47.4.10, --fml.mcVersion, 1.20.1, --fml.forgeGroup, net.minecraftforge, --fml.mcpVersion, 20230612.114412, --nogui]
[21:44:51] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher 10.0.9+10.0.9+main.dcd20f30 starting: java version 17.0.20 by Eclipse Adoptium; OS Linux arch aarch64 version 6.8.0-1054-oracle
[21:44:52] [main/INFO] [ne.mi.fm.lo.ImmediateWindowHandler/]: ImmediateWindowProvider not loading because launch target is forgeserver
[21:44:52] [main/INFO] [mixin/]: SpongePowered MIXIN Subsystem Version=0.8.5 Source=union:/opt/mcmgr/server/libraries/org/spongepowered/mixin/0.8.5/mixin-0.8.5.jar%2365!/ Service=ModLauncher Env=SERVER
[21:44:52] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/fmlcore/1.20.1-47.4.10/fmlcore-1.20.1-47.4.10.jar is missing mods.toml file
[21:44:52] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/javafmllanguage/1.20.1-47.4.10/javafmllanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:44:52] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/lowcodelanguage/1.20.1-47.4.10/lowcodelanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:44:52] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/mclanguage/1.20.1-47.4.10/mclanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:44:53] [main/INFO] [ne.mi.fm.lo.mo.JarInJarDependencyLocator/]: Found 41 dependencies adding them to mods collection
[21:44:57] [main/INFO] [mixin/]: Compatibility level set to JAVA_17
[21:44:57] [main/INFO] [cp.mo.mo.LaunchServiceHandler/MODLAUNCHER]: Launching target 'forgeserver' with arguments [--nogui]
[21:44:57] [main/INFO] [ModernFix/]: Loaded configuration file for ModernFix 5.27.58+mc1.20.1: 110 options available, 0 override(s) found
[21:44:57] [main/INFO] [ModernFix/]: Applying Nashorn fix
[21:44:57] [main/INFO] [ModernFix/]: Applied Forge config corruption patch
[21:44:57] [main/WARN] [mixin/]: Reference map 'yungsextras.refmap.json' for yungsextras.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:57] [main/WARN] [mixin/]: Reference map 'yungsextras.refmap.json' for yungsextras_forge.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:57] [main/WARN] [mixin/]: Reference map 'nitrogen_internals.refmap.json' for nitrogen_internals.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:57] [main/WARN] [mixin/]: Reference map 'kelvin-forge-refmap.json' for kelvin.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:57] [main/WARN] [mixin/]: Reference map 'kelvin-common-refmap.json' for kelvin-common.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:57] [main/WARN] [mixin/]: Reference map 'Create-The_Factory_Must_Grow.refmap.json' for tfmg.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:58] [main/INFO] [STDOUT/]: [org.valkyrienskies.mod.forge.mixin.ValkyrienForgeMixinConfigPlugin:onLoad:32]: six-seven:
[21:44:58] [main/INFO] [STDOUT/]: [org.valkyrienskies.mod.forge.mixin.ValkyrienForgeMixinConfigPlugin:onLoad:33]: true
[21:44:58] [main/WARN] [mixin/]: Reference map 'mixins.trackwork.refmap.json' for trackwork.mixins.json could not be read. If this is a development environment you can ignore this message
[21:44:59] [main/WARN] [mixin/]: Error loading class: dev/tr7zw/skinlayers/render/CustomizableModelPart (java.lang.ClassNotFoundException: dev.tr7zw.skinlayers.render.CustomizableModelPart)
[21:44:59] [main/WARN] [mixin/]: Error loading class: me/jellysquid/mods/sodium/client/render/vertex/buffer/SodiumBufferBuilder (java.lang.ClassNotFoundException: me.jellysquid.mods.sodium.client.render.vertex.buffer.SodiumBufferBuilder)
[21:45:00] [main/WARN] [mixin/]: Error loading class: com/teammetallurgy/aquaculture/entity/AquaFishingBobberEntity (java.lang.ClassNotFoundException: com.teammetallurgy.aquaculture.entity.AquaFishingBobberEntity)
[21:45:00] [main/ERROR] [ne.mi.fm.lo.RuntimeDistCleaner/DISTXFORM]: Attempted to load class net/minecraft/client/multiplayer/MultiPlayerGameMode for invalid dist DEDICATED_SERVER
[21:45:00] [main/WARN] [mixin/]: Error loading class: net/minecraft/client/multiplayer/MultiPlayerGameMode (java.lang.RuntimeException: Attempted to load class net/minecraft/client/multiplayer/MultiPlayerGameMode for invalid dist DEDICATED_SERVER)
[21:45:00] [main/WARN] [mixin/]: @Mixin target net.minecraft.client.multiplayer.MultiPlayerGameMode was not found mixins.cofhcore.json:MultiPlayerGameModeMixin
[21:45:00] [main/ERROR] [ne.mi.fm.lo.RuntimeDistCleaner/DISTXFORM]: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:45:00] [main/INFO] [mixin/]: Instancing error handler class org.valkyrienskies.mod.mixin.ValkyrienMixinErrorHandler
[21:45:00] [main/FATAL] [mixin/]: Mixin prepare failed preparing LivingEntityMixin in holdmyitems.mixins.json: org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
	at org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:864) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinConfig.prepareMixins(MixinConfig.java:850) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinConfig.prepare(MixinConfig.java:775) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:539) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.select(MixinProcessor.java:462) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.checkSelect(MixinProcessor.java:438) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.applyMixins(MixinProcessor.java:290) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinTransformer.transformClass(MixinTransformer.java:250) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.service.modlauncher.MixinTransformationHandler.processClass(MixinTransformationHandler.java:131) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.launch.MixinLaunchPluginLegacy.processClass(MixinLaunchPluginLegacy.java:131) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at cpw.mods.modlauncher.serviceapi.ILaunchPluginService.processClassWithFlags(ILaunchPluginService.java:156) ~[modlauncher-10.0.9.jar%2355!/:10.0.9+10.0.9+main.dcd20f30] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.cl.ModuleClassLoader.readerToClass(ModuleClassLoader.java:113) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.lambda$findClass$15(ModuleClassLoader.java:219) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.loadFromModule(ModuleClassLoader.java:229) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.findClass(ModuleClassLoader.java:219) ~[securejarhandler-2.1.10.jar:?] {}
	at java.lang.ClassLoader.loadClass(Unknown Source) ~[?:?] {}
	at java.lang.Class.forName(Unknown Source) ~[?:?] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.lambda$updateModuleReads$1(ImmediateWindowHandler.java:145) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at java.util.Optional.map(Unknown Source) ~[?:?] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.updateModuleReads(ImmediateWindowHandler.java:145) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler.acceptGameLayer(ImmediateWindowHandler.java:71) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.FMLLoader.beforeStart(FMLLoader.java:216) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.targets.CommonLaunchHandler.launchService(CommonLaunchHandler.java:92) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:30) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:53) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:71) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.Launcher.run(Launcher.java:108) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.Launcher.main(Launcher.java:78) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:26) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:23) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.bootstraplauncher.BootstrapLauncher.main(BootstrapLauncher.java:141) ~[bootstraplauncher-1.1.2.jar:?] {}
Caused by: java.lang.RuntimeException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
	at net.minecraftforge.fml.loading.RuntimeDistCleaner.processClassWithFlags(RuntimeDistCleaner.java:57) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.cl.ModuleClassLoader.getMaybeTransformedClassBytes(ModuleClassLoader.java:250) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.buildTransformedClassNodeFor(TransformingClassLoader.java:58) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.lambda$announceLaunch$10(LaunchPluginHandler.java:100) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at org.spongepowered.asm.launch.MixinLaunchPluginLegacy.getClassNode(MixinLaunchPluginLegacy.java:222) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinInfo.loadMixinClass(MixinInfo.java:1311) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:857) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	... 33 more
Exception in thread "main" [21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: java.lang.RuntimeException: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [holdmyitems.mixins.json:LivingEntityMixin] from phase [DEFAULT] in config [holdmyitems.mixins.json] FAILED during PREPARE
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:32)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:53)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:71)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.Launcher.run(Launcher.java:108)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.Launcher.main(Launcher.java:78)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:26)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:23)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.bootstraplauncher@1.1.2/cpw.mods.bootstraplauncher.BootstrapLauncher.main(BootstrapLauncher.java:141)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: Caused by: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [holdmyitems.mixins.json:LivingEntityMixin] from phase [DEFAULT] in config [holdmyitems.mixins.json] FAILED during PREPARE
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.handleMixinError(MixinProcessor.java:636)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.handleMixinPrepareError(MixinProcessor.java:584)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:542)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.select(MixinProcessor.java:462)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.checkSelect(MixinProcessor.java:438)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.applyMixins(MixinProcessor.java:290)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinTransformer.transformClass(MixinTransformer.java:250)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.service.modlauncher.MixinTransformationHandler.processClass(MixinTransformationHandler.java:131)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.launch.MixinLaunchPluginLegacy.processClass(MixinLaunchPluginLegacy.java:131)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.serviceapi.ILaunchPluginService.processClassWithFlags(ILaunchPluginService.java:156)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.readerToClass(ModuleClassLoader.java:113)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.lambda$findClass$15(ModuleClassLoader.java:219)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.loadFromModule(ModuleClassLoader.java:229)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.findClass(ModuleClassLoader.java:219)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.lang.Class.forName(Unknown Source)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.lambda$updateModuleReads$1(ImmediateWindowHandler.java:145)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.util.Optional.map(Unknown Source)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.updateModuleReads(ImmediateWindowHandler.java:145)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler.acceptGameLayer(ImmediateWindowHandler.java:71)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.FMLLoader.beforeStart(FMLLoader.java:216)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.targets.CommonLaunchHandler.launchService(CommonLaunchHandler.java:92)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:30)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	... 7 more
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: Caused by: org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:864)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinConfig.prepareMixins(MixinConfig.java:850)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinConfig.prepare(MixinConfig.java:775)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:539)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	... 30 more
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: Caused by: java.lang.RuntimeException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.RuntimeDistCleaner.processClassWithFlags(RuntimeDistCleaner.java:57)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.getMaybeTransformedClassBytes(ModuleClassLoader.java:250)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.buildTransformedClassNodeFor(TransformingClassLoader.java:58)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.lambda$announceLaunch$10(LaunchPluginHandler.java:100)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.launch.MixinLaunchPluginLegacy.getClassNode(MixinLaunchPluginLegacy.java:222)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.loadMixinClass(MixinInfo.java:1311)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:857)
[21:45:00] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	... 33 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Consumed 34.559s CPU time.
```

### Test 2
- Mod Pack: `custom-forge-1.20.1-MilesPackV2.zip`
- Pre-change Summary:
```
This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the server. If the server fails to start, check this skipped list first. Examples: embeddium-0.3.31+mc1.20.1.jar, entity_model_features-3.2.4-1.20.1-forge.jar, entityculling-forge-1.10.5-mc1.20.1.jar, mob_grinding_utils-1.20.1-1.1.0.jar, notenoughanimations-forge-1.12.4-mc1.20.1.jar, oculus-mc1.20.1-1.8.0.jar (and 1 more).

Some jar files in this zip do not declare whether they are client-only or server-side. Setup will keep them on the server after the exclude list and in-jar client strips. If the server fails to start, check those jars first. Examples: aether-1.20.1-1.5.2-neoforge.jar, appliedenergistics2-forge-15.4.10.jar, betterdays-1.20.1-3.3.4.5-FORGE.jar, BiomesOPlenty-forge-1.20.1-19.0.0.96.jar, born_in_chaos_[Forge]1.20.1_1.7.4.jar, chunkloaders-1.2.9-forge-mc1.20.1.jar (and 61 more).

Pack: custom-forge-1.20.1-MilesPackV2
Kind: UnstructuredServer
Minecraft: 1.20.1
Loader: forge
Required Java: 17
Root jars install into mods/.
Files in zip: 85
  Server-side jars: 70
  Client-only (not installed on the server): 7
    In-jar metadata: 0
    Override list: 7
  No side metadata (kept): 67
Override-list skipped jars:
  embeddium-0.3.31+mc1.20.1.jar
  entity_model_features-3.2.4-1.20.1-forge.jar
  entityculling-forge-1.10.5-mc1.20.1.jar
  mob_grinding_utils-1.20.1-1.1.0.jar
  notenoughanimations-forge-1.12.4-mc1.20.1.jar
  oculus-mc1.20.1-1.8.0.jar
  sound-physics-remastered-forge-1.20.1-1.4.10.jar
Warnings:
  7 jar(s) skipped by the CurseForge exclude list (known client-only).
  67 jar(s) have no in-jar side metadata; kept (server pack assumed). This is not a Modrinth env.server strip.
  Archive has jars at the root (no mods/ folder); they will install into mods/.
```
- Result: `FAIL`
- Fail Message: `Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.`
- Console Output: 
```
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
2026-08-21 21:52:25,388 main WARN Advanced terminal features are not available in this environment
[21:52:25] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher running: args [--launchTarget, forgeserver, --fml.forgeVersion, 47.4.10, --fml.mcVersion, 1.20.1, --fml.forgeGroup, net.minecraftforge, --fml.mcpVersion, 20230612.114412, --nogui]
[21:52:25] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher 10.0.9+10.0.9+main.dcd20f30 starting: java version 17.0.20 by Eclipse Adoptium; OS Linux arch aarch64 version 6.8.0-1054-oracle
[21:52:26] [main/INFO] [ne.mi.fm.lo.ImmediateWindowHandler/]: ImmediateWindowProvider not loading because launch target is forgeserver
[21:52:26] [main/INFO] [mixin/]: SpongePowered MIXIN Subsystem Version=0.8.5 Source=union:/opt/mcmgr/server/libraries/org/spongepowered/mixin/0.8.5/mixin-0.8.5.jar%2365!/ Service=ModLauncher Env=SERVER
[21:52:27] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/fmlcore/1.20.1-47.4.10/fmlcore-1.20.1-47.4.10.jar is missing mods.toml file
[21:52:27] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/javafmllanguage/1.20.1-47.4.10/javafmllanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:52:27] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/lowcodelanguage/1.20.1-47.4.10/lowcodelanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:52:27] [main/WARN] [ne.mi.fm.lo.mo.ModFileParser/LOADING]: Mod file /opt/mcmgr/server/libraries/net/minecraftforge/mclanguage/1.20.1-47.4.10/mclanguage-1.20.1-47.4.10.jar is missing mods.toml file
[21:52:27] [main/INFO] [ne.mi.fm.lo.mo.JarInJarDependencyLocator/]: Found 41 dependencies adding them to mods collection
[21:52:30] [main/INFO] [mixin/]: Compatibility level set to JAVA_17
[21:52:30] [main/INFO] [cp.mo.mo.LaunchServiceHandler/MODLAUNCHER]: Launching target 'forgeserver' with arguments [--nogui]
[21:52:30] [main/INFO] [ModernFix/]: Loaded configuration file for ModernFix 5.27.58+mc1.20.1: 110 options available, 0 override(s) found
[21:52:30] [main/INFO] [ModernFix/]: Applying Nashorn fix
[21:52:30] [main/INFO] [ModernFix/]: Applied Forge config corruption patch
[21:52:31] [main/WARN] [mixin/]: Reference map 'yungsextras.refmap.json' for yungsextras.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/WARN] [mixin/]: Reference map 'yungsextras.refmap.json' for yungsextras_forge.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/WARN] [mixin/]: Reference map 'nitrogen_internals.refmap.json' for nitrogen_internals.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/WARN] [mixin/]: Reference map 'kelvin-forge-refmap.json' for kelvin.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/WARN] [mixin/]: Reference map 'kelvin-common-refmap.json' for kelvin-common.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/WARN] [mixin/]: Reference map 'Create-The_Factory_Must_Grow.refmap.json' for tfmg.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:31] [main/INFO] [STDOUT/]: [org.valkyrienskies.mod.forge.mixin.ValkyrienForgeMixinConfigPlugin:onLoad:32]: six-seven:
[21:52:31] [main/INFO] [STDOUT/]: [org.valkyrienskies.mod.forge.mixin.ValkyrienForgeMixinConfigPlugin:onLoad:33]: true
[21:52:31] [main/WARN] [mixin/]: Reference map 'mixins.trackwork.refmap.json' for trackwork.mixins.json could not be read. If this is a development environment you can ignore this message
[21:52:32] [main/WARN] [mixin/]: Error loading class: dev/tr7zw/skinlayers/render/CustomizableModelPart (java.lang.ClassNotFoundException: dev.tr7zw.skinlayers.render.CustomizableModelPart)
[21:52:32] [main/WARN] [mixin/]: Error loading class: me/jellysquid/mods/sodium/client/render/vertex/buffer/SodiumBufferBuilder (java.lang.ClassNotFoundException: me.jellysquid.mods.sodium.client.render.vertex.buffer.SodiumBufferBuilder)
[21:52:32] [main/WARN] [mixin/]: Error loading class: com/teammetallurgy/aquaculture/entity/AquaFishingBobberEntity (java.lang.ClassNotFoundException: com.teammetallurgy.aquaculture.entity.AquaFishingBobberEntity)
[21:52:32] [main/ERROR] [ne.mi.fm.lo.RuntimeDistCleaner/DISTXFORM]: Attempted to load class net/minecraft/client/multiplayer/MultiPlayerGameMode for invalid dist DEDICATED_SERVER
[21:52:32] [main/WARN] [mixin/]: Error loading class: net/minecraft/client/multiplayer/MultiPlayerGameMode (java.lang.RuntimeException: Attempted to load class net/minecraft/client/multiplayer/MultiPlayerGameMode for invalid dist DEDICATED_SERVER)
[21:52:32] [main/WARN] [mixin/]: @Mixin target net.minecraft.client.multiplayer.MultiPlayerGameMode was not found mixins.cofhcore.json:MultiPlayerGameModeMixin
[21:52:32] [main/ERROR] [ne.mi.fm.lo.RuntimeDistCleaner/DISTXFORM]: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:52:32] [main/INFO] [mixin/]: Instancing error handler class org.valkyrienskies.mod.mixin.ValkyrienMixinErrorHandler
[21:52:32] [main/FATAL] [mixin/]: Mixin prepare failed preparing LivingEntityMixin in holdmyitems.mixins.json: org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
	at org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:864) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinConfig.prepareMixins(MixinConfig.java:850) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinConfig.prepare(MixinConfig.java:775) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:539) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.select(MixinProcessor.java:462) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.checkSelect(MixinProcessor.java:438) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinProcessor.applyMixins(MixinProcessor.java:290) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinTransformer.transformClass(MixinTransformer.java:250) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.service.modlauncher.MixinTransformationHandler.processClass(MixinTransformationHandler.java:131) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.launch.MixinLaunchPluginLegacy.processClass(MixinLaunchPluginLegacy.java:131) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at cpw.mods.modlauncher.serviceapi.ILaunchPluginService.processClassWithFlags(ILaunchPluginService.java:156) ~[modlauncher-10.0.9.jar%2355!/:10.0.9+10.0.9+main.dcd20f30] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.cl.ModuleClassLoader.readerToClass(ModuleClassLoader.java:113) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.lambda$findClass$15(ModuleClassLoader.java:219) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.loadFromModule(ModuleClassLoader.java:229) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.cl.ModuleClassLoader.findClass(ModuleClassLoader.java:219) ~[securejarhandler-2.1.10.jar:?] {}
	at java.lang.ClassLoader.loadClass(Unknown Source) ~[?:?] {}
	at java.lang.Class.forName(Unknown Source) ~[?:?] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.lambda$updateModuleReads$1(ImmediateWindowHandler.java:145) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at java.util.Optional.map(Unknown Source) ~[?:?] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.updateModuleReads(ImmediateWindowHandler.java:145) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.ImmediateWindowHandler.acceptGameLayer(ImmediateWindowHandler.java:71) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.FMLLoader.beforeStart(FMLLoader.java:216) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at net.minecraftforge.fml.loading.targets.CommonLaunchHandler.launchService(CommonLaunchHandler.java:92) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:30) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:53) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:71) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.Launcher.run(Launcher.java:108) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.Launcher.main(Launcher.java:78) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:26) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:23) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.bootstraplauncher.BootstrapLauncher.main(BootstrapLauncher.java:141) ~[bootstraplauncher-1.1.2.jar:?] {}
Caused by: java.lang.RuntimeException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
	at net.minecraftforge.fml.loading.RuntimeDistCleaner.processClassWithFlags(RuntimeDistCleaner.java:57) ~[fmlloader-1.20.1-47.4.10.jar%2369!/:1.0] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.cl.ModuleClassLoader.getMaybeTransformedClassBytes(ModuleClassLoader.java:250) ~[securejarhandler-2.1.10.jar:?] {}
	at cpw.mods.modlauncher.TransformingClassLoader.buildTransformedClassNodeFor(TransformingClassLoader.java:58) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at cpw.mods.modlauncher.LaunchPluginHandler.lambda$announceLaunch$10(LaunchPluginHandler.java:100) ~[modlauncher-10.0.9.jar%2355!/:?] {}
	at org.spongepowered.asm.launch.MixinLaunchPluginLegacy.getClassNode(MixinLaunchPluginLegacy.java:222) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinInfo.loadMixinClass(MixinInfo.java:1311) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	at org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:857) ~[mixin-0.8.5.jar%2365!/:0.8.5+Jenkins-b310.git-155314e6e91465dad727e621a569906a410cd6f4] {}
	... 33 more
Exception in thread "main" [21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: java.lang.RuntimeException: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [holdmyitems.mixins.json:LivingEntityMixin] from phase [DEFAULT] in config [holdmyitems.mixins.json] FAILED during PREPARE
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:32)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:53)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandler.launch(LaunchServiceHandler.java:71)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.Launcher.run(Launcher.java:108)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.Launcher.main(Launcher.java:78)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:26)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.BootstrapLaunchConsumer.accept(BootstrapLaunchConsumer.java:23)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.bootstraplauncher@1.1.2/cpw.mods.bootstraplauncher.BootstrapLauncher.main(BootstrapLauncher.java:141)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: Caused by: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [holdmyitems.mixins.json:LivingEntityMixin] from phase [DEFAULT] in config [holdmyitems.mixins.json] FAILED during PREPARE
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.handleMixinError(MixinProcessor.java:636)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.handleMixinPrepareError(MixinProcessor.java:584)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:542)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.select(MixinProcessor.java:462)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.checkSelect(MixinProcessor.java:438)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.applyMixins(MixinProcessor.java:290)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinTransformer.transformClass(MixinTransformer.java:250)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.service.modlauncher.MixinTransformationHandler.processClass(MixinTransformationHandler.java:131)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.launch.MixinLaunchPluginLegacy.processClass(MixinLaunchPluginLegacy.java:131)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.serviceapi.ILaunchPluginService.processClassWithFlags(ILaunchPluginService.java:156)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.readerToClass(ModuleClassLoader.java:113)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.lambda$findClass$15(ModuleClassLoader.java:219)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.loadFromModule(ModuleClassLoader.java:229)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.findClass(ModuleClassLoader.java:219)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.lang.Class.forName(Unknown Source)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.lambda$updateModuleReads$1(ImmediateWindowHandler.java:145)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at java.base/java.util.Optional.map(Unknown Source)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler$DummyProvider.updateModuleReads(ImmediateWindowHandler.java:145)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.ImmediateWindowHandler.acceptGameLayer(ImmediateWindowHandler.java:71)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.FMLLoader.beforeStart(FMLLoader.java:216)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.targets.CommonLaunchHandler.launchService(CommonLaunchHandler.java:92)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchServiceHandlerDecorator.launch(LaunchServiceHandlerDecorator.java:30)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.ThreadGroup:uncaughtException:-1]: 	... 7 more
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: Caused by: org.spongepowered.asm.mixin.transformer.throwables.InvalidMixinException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:864)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinConfig.prepareMixins(MixinConfig.java:850)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinConfig.prepare(MixinConfig.java:775)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinProcessor.prepareConfigs(MixinProcessor.java:539)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	... 30 more
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: Caused by: java.lang.RuntimeException: Attempted to load class de/bene2212/holdmyitems/mixin/LivingEntityMixin for invalid dist DEDICATED_SERVER
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/fmlloader@1.20.1-47.4.10/net.minecraftforge.fml.loading.RuntimeDistCleaner.processClassWithFlags(RuntimeDistCleaner.java:57)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.offerClassNodeToPlugins(LaunchPluginHandler.java:88)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.ClassTransformer.transform(ClassTransformer.java:120)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.maybeTransformClassBytes(TransformingClassLoader.java:50)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at cpw.mods.securejarhandler/cpw.mods.cl.ModuleClassLoader.getMaybeTransformedClassBytes(ModuleClassLoader.java:250)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.TransformingClassLoader.buildTransformedClassNodeFor(TransformingClassLoader.java:58)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/cpw.mods.modlauncher@10.0.9/cpw.mods.modlauncher.LaunchPluginHandler.lambda$announceLaunch$10(LaunchPluginHandler.java:100)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.launch.MixinLaunchPluginLegacy.getClassNode(MixinLaunchPluginLegacy.java:222)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.loadMixinClass(MixinInfo.java:1311)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	at MC-BOOTSTRAP/org.spongepowered.mixin/org.spongepowered.asm.mixin.transformer.MixinInfo.<init>(MixinInfo.java:857)
[21:52:32] [main/INFO] [STDERR/]: [java.lang.Throwable:printStackTrace:-1]: 	... 33 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Consumed 32.141s CPU time.
minecraft.service: Scheduled restart job, restart counter is at 5.
Stopped Minecraft server.
minecraft.service: Consumed 32.141s CPU time.
```


### Test 3
- Mod Pack: `modrinth-fabric-Fabulously.Optimized-v6.5.0.mrpack`
- Pre-change Summary:
```
This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the server. If the server fails to start, check this skipped list first. Examples: BetterGrassify-1.8.6+fabric.1.21.1.jar, CrashAssistant-fabric-1.20.2-1.21.4-1.11.9.jar, ImmediatelyFast-Fabric-1.6.10+1.21.1.jar, citresewn-1.2.2+1.21.jar, continuity-3.0.0+1.21.jar, cwb-fabric-3.0.0+mc1.21.jar (and 11 more).

Pack: Fabulously Optimized (6.5.0)
Minecraft: 1.21.1
Loader: fabric 0.19.3
Required Java: 21
Files in pack: 50
  Server-side: 33 (33 required, 0 optional)
  Client-only (not installed on the server): 17
    Pack-declared: 0
    Override list: 17
  Side unclear: 0
Override-list skipped files:
  mods/BetterGrassify-1.8.6+fabric.1.21.1.jar
  mods/CrashAssistant-fabric-1.20.2-1.21.4-1.11.9.jar
  mods/ImmediatelyFast-Fabric-1.6.10+1.21.1.jar
  mods/citresewn-1.2.2+1.21.jar
  mods/continuity-3.0.0+1.21.jar
  mods/cwb-fabric-3.0.0+mc1.21.jar
  mods/entity_model_features-3.2.4-1.21-fabric.jar
  mods/entity_texture_features_1.21-fabric-7.1.jar
  mods/entityculling-fabric-1.10.2-mc1.21.1.jar
  mods/fastquit-3.0.0+1.20.6.jar
  mods/iris-fabric-1.8.8+mc1.21.1.jar
  mods/language-reload-1.7.6+1.21.1.jar
  mods/reeses-sodium-options-fabric-1.8.3+mc1.21.4.jar
  mods/sodium-extra-fabric-0.6.0+mc1.21.1.jar
  mods/sodium-fabric-0.6.13+mc1.21.1.jar
  mods/zoomify-2.15.2+1.21.1.jar
  resourcepacks/SodiumTranslations.zip
Overrides: overrides/ (client+server)
```
- Result: `SUCCESS`
- Fail Message: `N/A`
- Console Output: 
```
[21:55:51] [Server thread/INFO]: Starting minecraft server version 1.21.1
[21:55:51] [Server thread/INFO]: Loading properties
[21:55:51] [Server thread/INFO]: Default game type: SURVIVAL
[21:55:51] [Server thread/INFO]: Generating keypair
[21:55:51] [Server thread/INFO]: Starting Minecraft server on *:25565
[21:55:51] [Server thread/INFO]: Using default channel type
[21:55:51] [Server thread/INFO]: Preparing level "world"
[21:55:51] [e4mc_minecraft-init/INFO]: broker req: https://broker.e4mc.link/getBestRelay GET
[21:55:52] [e4mc_minecraft-init/INFO]: broker resp: (GET https://broker.e4mc.link/getBestRelay) 200
[21:55:52] [e4mc_minecraft-init/INFO]: using relay us
[21:55:52] [e4mc_minecraft-init/ERROR]: error in e4mc
java.lang.UnsatisfiedLinkError: failed to load the required native library
	at knot/io.netty.incubator.codec.quic.Quic.ensureAvailability(Quic.java:87) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<init>(QuicheQuicSslContext.java:164) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:401) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:229) ~[e4mc-fabric-6.1.1.jar:?]
	at java.base/java.lang.Thread.run(Unknown Source) [?:?]
Caused by: java.lang.ExceptionInInitializerError
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	... 2 more
Caused by: java.lang.RuntimeException: java.nio.file.FileSystemException: /tmp/e4mc_temp3182731856976986938: Read-only file system
	at knot/io.netty.incubator.codec.quic.Quiche.loadNativeLibrary(Quiche.java:142) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quiche.<clinit>(Quiche.java:69) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	... 2 more
Caused by: java.nio.file.FileSystemException: /tmp/e4mc_temp3182731856976986938: Read-only file system
	at java.base/sun.nio.fs.UnixException.translateToIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixException.rethrowAsIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixException.rethrowAsIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixFileSystemProvider.createDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.Files.createDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.TempFileHelper.create(Unknown Source) ~[?:?]
	at java.base/java.nio.file.TempFileHelper.createTempDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.Files.createTempDirectory(Unknown Source) ~[?:?]
	at knot/io.netty.incubator.codec.quic.Quiche.loadNativeLibrary(Quiche.java:113) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quiche.<clinit>(Quiche.java:69) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	... 2 more
[21:55:52] [e4mc_minecraft-init/ERROR]: Uncaught exception in thread "e4mc_minecraft-init"
java.lang.RuntimeException: java.lang.UnsatisfiedLinkError: failed to load the required native library
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:394) ~[e4mc-fabric-6.1.1.jar:?]
	at java.base/java.lang.Thread.run(Unknown Source) [?:?]
Caused by: java.lang.UnsatisfiedLinkError: failed to load the required native library
	at knot/io.netty.incubator.codec.quic.Quic.ensureAvailability(Quic.java:87) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<init>(QuicheQuicSslContext.java:164) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:401) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:229) ~[e4mc-fabric-6.1.1.jar:?]
	... 1 more
Caused by: java.lang.ExceptionInInitializerError
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:229) ~[e4mc-fabric-6.1.1.jar:?]
	... 1 more
Caused by: java.lang.RuntimeException: java.nio.file.FileSystemException: /tmp/e4mc_temp3182731856976986938: Read-only file system
	at knot/io.netty.incubator.codec.quic.Quiche.loadNativeLibrary(Quiche.java:142) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quiche.<clinit>(Quiche.java:69) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:229) ~[e4mc-fabric-6.1.1.jar:?]
	... 1 more
Caused by: java.nio.file.FileSystemException: /tmp/e4mc_temp3182731856976986938: Read-only file system
	at java.base/sun.nio.fs.UnixException.translateToIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixException.rethrowAsIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixException.rethrowAsIOException(Unknown Source) ~[?:?]
	at java.base/sun.nio.fs.UnixFileSystemProvider.createDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.Files.createDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.TempFileHelper.create(Unknown Source) ~[?:?]
	at java.base/java.nio.file.TempFileHelper.createTempDirectory(Unknown Source) ~[?:?]
	at java.base/java.nio.file.Files.createTempDirectory(Unknown Source) ~[?:?]
	at knot/io.netty.incubator.codec.quic.Quiche.loadNativeLibrary(Quiche.java:113) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quiche.<clinit>(Quiche.java:69) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.Quic.<clinit>(Quic.java:46) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicheQuicSslContext.<clinit>(QuicheQuicSslContext.java:85) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/io.netty.incubator.codec.quic.QuicSslContextBuilder.build(QuicSslContextBuilder.java:399) ~[e4mc-fabric-6.1.1.jar:?]
	at knot/link.e4mc.QuiclimeSession.start(QuiclimeSession.java:229) ~[e4mc-fabric-6.1.1.jar:?]
	... 1 more
[21:55:54] [Server thread/INFO]: Preparing start region for dimension minecraft:overworld
[21:55:54] [Server thread/WARN]: Method overwrite conflict for removeIf in modernfix-common.mixins.json:bugfix.paper_chunk_patches.SortedArraySetMixin from mod modernfix, previously written by net.caffeinemc.mods.lithium.mixin.collections.chunk_tickets.SortedArraySetMixin. Skipping method.
[21:55:55] [Worker-Main-2/INFO]: Preparing spawn area: 0%
[21:55:55] [Worker-Main-2/INFO]: Preparing spawn area: 0%
[21:55:55] [Server thread/INFO]: Time elapsed: 802 ms
[21:55:55] [Server thread/INFO]: Done (3.507s)! For help, type "help"
[21:55:55] [Server thread/INFO]: Starting remote control listener
[21:55:55] [Server thread/WARN]: Dedicated server took 13.699 seconds to load
[21:55:55] [Server thread/ERROR]: Couldn't load server icon
```


### Test 4
- Mod Pack: `modrinth-fabric-OptiFine-for-Fabric-4+21.1.mrpack`
- Pre-change Summary:
```
This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the server. If the server fails to start, check this skipped list first. Examples: BadOptimizations-2.3.0-1.21.1.jar, reeses-sodium-options-fabric-1.8.3+mc1.21.4.jar, entity_texture_features_1.21-fabric-7.0.2.jar, sodiumdynamiclights-fabric-1.0.10-1.21.1.jar, fastquit-3.0.0+1.20.6.jar, fast-ip-ping-v1.0.7-mc1.21.1-fabric.jar (and 16 more).

Pack: OptiFine for Fabric 4 [1.21.1] (4+21.1)
Minecraft: 1.21.1
Loader: fabric 0.16.14
Required Java: 21
Files in pack: 69
  Server-side: 47 (47 required, 0 optional)
  Client-only (not installed on the server): 22
    Pack-declared: 0
    Override list: 22
  Side unclear: 0
Override-list skipped files:
  mods/BadOptimizations-2.3.0-1.21.1.jar
  mods/reeses-sodium-options-fabric-1.8.3+mc1.21.4.jar
  mods/entity_texture_features_1.21-fabric-7.0.2.jar
  mods/sodiumdynamiclights-fabric-1.0.10-1.21.1.jar
  mods/fastquit-3.0.0+1.20.6.jar
  mods/fast-ip-ping-v1.0.7-mc1.21.1-fabric.jar
  mods/sodiumextras-fabric-1.0.8-1.21.1.jar
  mods/sodiumcoreshadersupport-1.3.4-mc1.21.1-sodium0.6.13-fabric.jar
  mods/cwb-fabric-3.0.0+mc1.21.jar
  mods/BetterGrassify-1.7.0+fabric.1.21.1.jar
  mods/continuity-3.0.0+1.21.jar
  mods/ImmediatelyFast-Fabric-1.6.6+1.21.1.jar
  mods/fancymenu_fabric_3.7.0_MC_1.21.1.jar
  mods/language-reload-1.7.4+1.21.1.jar
  mods/iris-fabric-1.8.8+mc1.21.1.jar
  mods/entityculling-fabric-1.8.2-mc1.21.jar
  mods/entity_model_features_1.21-fabric-3.0.1.jar
  mods/sodium-fabric-0.6.13+mc1.21.1.jar
  mods/SodiumExtraInformation-fabric-2.5.jar
  mods/sodium-extra-fabric-0.6.0+mc1.21.1.jar
  mods/sodium-shadowy-path-blocks-fabric-4.0.0.jar
  mods/Zoomify-2.14.4+1.21.1.jar
Overrides: overrides/ (client+server)
```
- Result: `FAIL`
- Fail Message: `Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.`
- Console Output: 
```
	   |-- c2me-fixes-chunkio-threading-issues 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-fixes-general-threading-issues 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-fixes-worldgen-threading-issues 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-fixes-worldgen-vanilla-bugs 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-notickvd 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-allocs 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-chunk-access 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-chunkio 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-dfc 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-math 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-scheduling 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-worldgen-general 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-opts-worldgen-vanilla 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-rewrites-chunk-serializer 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-rewrites-chunk-system 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-rewrites-chunkio 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-server-utils 0.3.0+alpha.0.320+1.21.1
	   |-- c2me-threading-lighting 0.3.0+alpha.0.320+1.21.1
	   |-- com_ibm_async_asyncutil 0.1.0
	   |-- io_reactivex_rxjava3_rxjava 3.1.8
	   |-- net_objecthunter_exp4j 0.4.8
	   |-- org_jctools_jctools-core 4.0.5
	   \-- org_reactivestreams_reactive-streams 1.0.4
	- cloth-config 15.0.140
	   \-- cloth-basic-math 0.6.1
	- fabric-api 0.116.6+1.21.1
	   |-- fabric-api-base 0.4.42+6573ed8c19
	   |-- fabric-api-lookup-api-v1 1.6.71+b559734419
	   |-- fabric-biome-api-v1 13.0.31+d527f9fd19
	   |-- fabric-block-api-v1 1.1.0+0bc3503219
	   |-- fabric-block-view-api-v2 1.0.11+ebb2264e19
	   |-- fabric-blockrenderlayer-v1 1.1.52+0af3f5a719
	   |-- fabric-client-tags-api-v1 1.1.15+6573ed8c19
	   |-- fabric-command-api-v1 1.2.49+f71b366f19
	   |-- fabric-command-api-v2 2.2.28+6ced4dd919
	   |-- fabric-commands-v0 0.2.66+df3654b319
	   |-- fabric-content-registries-v0 8.0.19+b559734419
	   |-- fabric-convention-tags-v1 2.1.5+7f945d5b19
	   |-- fabric-convention-tags-v2 2.11.1+a406e79519
	   |-- fabric-crash-report-info-v1 0.2.29+0af3f5a719
	   |-- fabric-data-attachment-api-v1 1.4.5+6116a37819
	   |-- fabric-data-generation-api-v1 20.2.33+37516cd619
	   |-- fabric-dimensions-v1 4.0.1+65213ef819
	   |-- fabric-entity-events-v1 1.8.0+2b27e0a419
	   |-- fabric-events-interaction-v0 0.7.13+ba9dae0619
	   |-- fabric-game-rule-api-v1 1.0.53+6ced4dd919
	   |-- fabric-item-api-v1 11.2.0+3b3cb2e819
	   |-- fabric-item-group-api-v1 4.1.7+def88e3a19
	   |-- fabric-key-binding-api-v1 1.0.47+0af3f5a719
	   |-- fabric-keybindings-v0 0.2.45+df3654b319
	   |-- fabric-lifecycle-events-v1 2.6.0+0865547519
	   |-- fabric-loot-api-v2 3.0.15+3f89f5a519
	   |-- fabric-loot-api-v3 1.0.3+3f89f5a519
	   |-- fabric-message-api-v1 6.0.14+8aaf3aca19
	   |-- fabric-model-loading-api-v1 2.1.0+b4d813fc19
	   |-- fabric-networking-api-v1 4.3.0+c7469b2119
	   |-- fabric-object-builder-api-v1 15.2.1+40875a9319
	   |-- fabric-particles-v1 4.0.2+6573ed8c19
	   |-- fabric-recipe-api-v1 5.0.14+248df81c19
	   |-- fabric-registry-sync-v0 5.3.1+e3eddc2119
	   |-- fabric-renderer-api-v1 3.4.1+b4d813fc19
	   |-- fabric-renderer-indigo 1.7.1+c705a49c19
	   |-- fabric-renderer-registries-v1 3.2.69+df3654b319
	   |-- fabric-rendering-data-attachment-v1 0.3.49+73761d2e19
	   |-- fabric-rendering-fluids-v1 3.1.6+1daea21519
	   |-- fabric-rendering-v0 1.1.72+df3654b319
	   |-- fabric-rendering-v1 5.1.0+ab4c25a019
	   |-- fabric-resource-conditions-api-v1 4.3.0+8dc279b119
	   |-- fabric-resource-loader-v0 1.3.1+5b5275af19
	   |-- fabric-screen-api-v1 2.0.25+8b68f1c719
	   |-- fabric-screen-handler-api-v1 1.3.90+b559734419
	   |-- fabric-sound-api-v1 1.0.23+6573ed8c19
	   |-- fabric-transfer-api-v1 5.4.3+c24bd99419
	   \-- fabric-transitive-access-wideners-v1 6.2.0+45b9699719
	- fabric-language-kotlin 1.13.6+kotlin.2.2.20
	   |-- org_jetbrains_kotlin_kotlin-reflect 2.2.20
	   |-- org_jetbrains_kotlin_kotlin-stdlib 2.2.20
	   |-- org_jetbrains_kotlin_kotlin-stdlib-jdk7 2.2.20
	   |-- org_jetbrains_kotlin_kotlin-stdlib-jdk8 2.2.20
	   |-- org_jetbrains_kotlinx_atomicfu-jvm 0.29.0
	   |-- org_jetbrains_kotlinx_kotlinx-coroutines-core-jvm 1.10.2
	   |-- org_jetbrains_kotlinx_kotlinx-coroutines-jdk8 1.10.2
	   |-- org_jetbrains_kotlinx_kotlinx-datetime-jvm 0.7.1
	   |-- org_jetbrains_kotlinx_kotlinx-io-bytestring-jvm 0.8.0
	   |-- org_jetbrains_kotlinx_kotlinx-io-core-jvm 0.8.0
	   |-- org_jetbrains_kotlinx_kotlinx-serialization-cbor-jvm 1.9.0
	   |-- org_jetbrains_kotlinx_kotlinx-serialization-core-jvm 1.9.0
	   \-- org_jetbrains_kotlinx_kotlinx-serialization-json-jvm 1.9.0
	- fabricloader 0.16.14
	   \-- mixinextras 0.4.1
	- ferritecore 7.0.2-hotfix
	- fzzy_config 0.7.2+1.21
	   |-- blue_endless_jankson 1.2.3
	   |-- fabric-permissions-api-v0 0.3.1
	   \-- net_peanuuutz_tomlkt_tomlkt-jvm 0.3.7
	- java 21
	- konkrete 1.9.9
	- krypton 0.2.8
	   \-- com_velocitypowered_velocity-native 3.3.0-SNAPSHOT
	- libjf 3.17.3
	   |-- libjf-base 3.17.3
	   |-- libjf-config-commands 3.17.3
	   |-- libjf-config-core-v2 3.17.3
	   |-- libjf-config-network-v0 3.17.3
	   |-- libjf-config-ui-tiny 3.17.3
	   |-- libjf-data-manipulation-v0 3.17.3
	   |-- libjf-data-v0 3.17.3
	   |-- libjf-mainhttp-v0 3.17.3
	   |-- libjf-resource-pack-entry-widgets-v0 3.17.3
	   |-- libjf-translate-v1 3.17.3
	   |-- libjf-unsafe-v0 3.17.3
	   \-- libjf-web-v1 3.17.3
	- lithium 0.15.0+mc1.21.1
	- melody 1.0.10
	- minecraft 1.21.1
	- mod-loading-screen 1.0.5
	   |-- com_formdev_flatlaf 3.5.4
	   \-- net_lenni0451_reflect 1.4.0
	- modernfix 5.24.3+mc1.21.1
	- noisium 2.3.0+mc1.21-1.21.1
	- placeholder-api 2.4.2+1.21
	- rpc 1.2.2
	- scalablelux 0.1.0.1+fabric.d0d58ab
	- titlebarchanger 0.4
	- txnilib 1.0.24
	   \-- forgeconfigapiport 21.1.0
	- vmp 0.2.0+beta.7.172+1.21.1
	   \-- com_ibm_async_asyncutil 0.1.0
	- yet_another_config_lib_v3 3.7.1+1.21.1-fabric
	   |-- com_twelvemonkeys_common_common-image 3.12.0
	   |-- com_twelvemonkeys_common_common-io 3.12.0
	   |-- com_twelvemonkeys_common_common-lang 3.12.0
	   |-- com_twelvemonkeys_imageio_imageio-core 3.12.0
	   |-- com_twelvemonkeys_imageio_imageio-metadata 3.12.0
	   |-- com_twelvemonkeys_imageio_imageio-webp 3.12.0
	   |-- org_quiltmc_parsers_gson 0.2.1
	   \-- org_quiltmc_parsers_json 0.2.1
Aug 21, 2026 10:03:23 PM io.gitlab.jfronny.libjf.unsafe.JfLanguageAdapter <clinit>
INFO: Starting unsafe init
Aug 21, 2026 10:03:23 PM io.gitlab.jfronny.libjf.unsafe.JfLanguageAdapter <clinit>
INFO: LibJF unsafe init completed
[ModLoadingScreen] I just want to say... I'm loading *really* early.
[ModLoadingScreen] Failed to initialize loading screen. Aborting!
[22:03:23] [main/ERROR]: Uncaught exception in thread "main"
java.lang.RuntimeException: An exception occurred when launching the server!
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:71) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.installer.ServerLauncher.main(ServerLauncher.java:69) ~[fabric-server-mc.1.21.1-loader.0.16.14-launcher.1.1.2.jar:1.1.2]
Caused by: java.lang.Error: java.lang.NoClassDefFoundError: net/fabricmc/loader/api/ModContainer
	at knot/io.github.gaming32.modloadingscreen.ModLoadingScreen.<clinit>(ModLoadingScreen.java:91) ~[mod-loading-screen-1.0.5.jar:?]
	at java.base/java.lang.Class.forName0(Native Method) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.setupLanguageAdapters(FabricLoaderImpl.java:488) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.finishModLoading(FabricLoaderImpl.java:367) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.freeze(FabricLoaderImpl.java:116) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:147) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:68) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69) ~[fabric-loader-0.16.14.jar:?]
	... 1 more
Caused by: java.lang.NoClassDefFoundError: net/fabricmc/loader/api/ModContainer
	at java.base/java.lang.Class.getDeclaredMethods0(Native Method) ~[?:?]
	at knot/net.lenni0451.reflect.Methods.getDeclaredMethods(Methods.java:42) ~[net_lenni0451_reflect-1.4.0-6d0be86487ce3ea4.jar:?]
	at knot/net.lenni0451.reflect.Methods.getDeclaredMethod(Methods.java:56) ~[net_lenni0451_reflect-1.4.0-6d0be86487ce3ea4.jar:?]
	at knot/io.github.gaming32.modloadingscreen.ModLoadingScreen.init(ModLoadingScreen.java:66) ~[mod-loading-screen-1.0.5.jar:?]
	at knot/io.github.gaming32.modloadingscreen.ModLoadingScreen.<clinit>(ModLoadingScreen.java:88) ~[mod-loading-screen-1.0.5.jar:?]
	at java.base/java.lang.Class.forName0(Native Method) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.setupLanguageAdapters(FabricLoaderImpl.java:488) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.finishModLoading(FabricLoaderImpl.java:367) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.freeze(FabricLoaderImpl.java:116) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:147) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:68) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69) ~[fabric-loader-0.16.14.jar:?]
	... 1 more
Caused by: java.lang.ClassNotFoundException: net.fabricmc.loader.api.ModContainer
	at java.base/jdk.internal.loader.BuiltinClassLoader.loadClass(Unknown Source) ~[?:?]
	at java.base/jdk.internal.loader.ClassLoaders$AppClassLoader.loadClass(Unknown Source) ~[?:?]
	at java.base/java.lang.ClassLoader.loadClass(Unknown Source) ~[?:?]
	at java.base/java.lang.Class.getDeclaredMethods0(Native Method) ~[?:?]
	at knot/net.lenni0451.reflect.Methods.getDeclaredMethods(Methods.java:42) ~[net_lenni0451_reflect-1.4.0-6d0be86487ce3ea4.jar:?]
	at knot/net.lenni0451.reflect.Methods.getDeclaredMethod(Methods.java:56) ~[net_lenni0451_reflect-1.4.0-6d0be86487ce3ea4.jar:?]
	at knot/io.github.gaming32.modloadingscreen.ModLoadingScreen.init(ModLoadingScreen.java:66) ~[mod-loading-screen-1.0.5.jar:?]
	at knot/io.github.gaming32.modloadingscreen.ModLoadingScreen.<clinit>(ModLoadingScreen.java:88) ~[mod-loading-screen-1.0.5.jar:?]
	at java.base/java.lang.Class.forName0(Native Method) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at java.base/java.lang.Class.forName(Unknown Source) ~[?:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.setupLanguageAdapters(FabricLoaderImpl.java:488) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.finishModLoading(FabricLoaderImpl.java:367) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.FabricLoaderImpl.freeze(FabricLoaderImpl.java:116) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:147) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:68) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23) ~[fabric-loader-0.16.14.jar:?]
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69) ~[fabric-loader-0.16.14.jar:?]
	... 1 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Consumed 3.872s CPU time.
```


### Test 5
- Mod Pack: `modrinth-fabric-Simply-Optimized-Continued-v2.1+26.2.mrpack`
- Pre-change Summary:
```
This pack marks some mods as needed on the server that are known client-only mods. Setup will skip those on the server. If the server fails to start, check this skipped list first. Examples: ImmediatelyFast-Fabric-1.16.3+26.2.jar, sodium-fabric-0.9.2-alpha.4+mc26.2.jar.

Pack: Simply Optimized Continued (2.1+26.2)
Minecraft: 26.2
Loader: fabric 0.19.3
Required Java: 25
Files in pack: 19
  Server-side: 16 (16 required, 0 optional)
  Client-only (not installed on the server): 3
    Pack-declared: 1
    Override list: 2
  Side unclear: 0
Pack-declared client-only files:
  mods/entityculling-fabric-1.10.5-mc26.2.jar
Override-list skipped files:
  mods/ImmediatelyFast-Fabric-1.16.3+26.2.jar
  mods/sodium-fabric-0.9.2-alpha.4+mc26.2.jar
Overrides: overrides/ (client+server)
```
- Result: `FAIL`
- Fail Message: `Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.`
- Console Output: 
```
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
[22:06:57] [ERROR] [FabricLoader/]: Uncaught exception in thread "main"
java.lang.RuntimeException: An exception occurred when launching the server!
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:71)
	at net.fabricmc.installer.ServerLauncher.main(ServerLauncher.java:69)
Caused by: java.lang.RuntimeException: Error invoking MC server bundler: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:102)
	at net.fabricmc.loader.impl.game.minecraft.MinecraftGameProvider.locateGame(MinecraftGameProvider.java:212)
	at net.fabricmc.loader.impl.launch.knot.Knot.createGameProvider(Knot.java:171)
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:128)
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:66)
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23)
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69)
	... 1 more
Caused by: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor$1.loadClass(BundlerProcessor.java:65)
	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
	at java.base/java.lang.Class.forName0(Native Method)
	at java.base/java.lang.Class.forName(Unknown Source)
	at java.base/java.lang.Class.forName(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:85)
	... 7 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Scheduled restart job, restart counter is at 5.
Stopped Minecraft server.
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
[22:07:15] [ERROR] [FabricLoader/]: Uncaught exception in thread "main"
java.lang.RuntimeException: An exception occurred when launching the server!
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:71)
	at net.fabricmc.installer.ServerLauncher.main(ServerLauncher.java:69)
Caused by: java.lang.RuntimeException: Error invoking MC server bundler: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:102)
	at net.fabricmc.loader.impl.game.minecraft.MinecraftGameProvider.locateGame(MinecraftGameProvider.java:212)
	at net.fabricmc.loader.impl.launch.knot.Knot.createGameProvider(Knot.java:171)
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:128)
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:66)
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23)
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69)
	... 1 more
Caused by: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor$1.loadClass(BundlerProcessor.java:65)
	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
	at java.base/java.lang.Class.forName0(Native Method)
	at java.base/java.lang.Class.forName(Unknown Source)
	at java.base/java.lang.Class.forName(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:85)
	... 7 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Scheduled restart job, restart counter is at 6.
Stopped Minecraft server.
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
[22:07:32] [ERROR] [FabricLoader/]: Uncaught exception in thread "main"
java.lang.RuntimeException: An exception occurred when launching the server!
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:71)
	at net.fabricmc.installer.ServerLauncher.main(ServerLauncher.java:69)
Caused by: java.lang.RuntimeException: Error invoking MC server bundler: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:102)
	at net.fabricmc.loader.impl.game.minecraft.MinecraftGameProvider.locateGame(MinecraftGameProvider.java:212)
	at net.fabricmc.loader.impl.launch.knot.Knot.createGameProvider(Knot.java:171)
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:128)
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:66)
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23)
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69)
	... 1 more
Caused by: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor$1.loadClass(BundlerProcessor.java:65)
	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
	at java.base/java.lang.Class.forName0(Native Method)
	at java.base/java.lang.Class.forName(Unknown Source)
	at java.base/java.lang.Class.forName(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:85)
	... 7 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Scheduled restart job, restart counter is at 7.
Stopped Minecraft server.
Started Minecraft server.
Picked up JAVA_TOOL_OPTIONS: -Djava.net.preferIPv4Stack=true
[22:07:50] [ERROR] [FabricLoader/]: Uncaught exception in thread "main"
java.lang.RuntimeException: An exception occurred when launching the server!
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:71)
	at net.fabricmc.installer.ServerLauncher.main(ServerLauncher.java:69)
Caused by: java.lang.RuntimeException: Error invoking MC server bundler: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:102)
	at net.fabricmc.loader.impl.game.minecraft.MinecraftGameProvider.locateGame(MinecraftGameProvider.java:212)
	at net.fabricmc.loader.impl.launch.knot.Knot.createGameProvider(Knot.java:171)
	at net.fabricmc.loader.impl.launch.knot.Knot.init(Knot.java:128)
	at net.fabricmc.loader.impl.launch.knot.Knot.launch(Knot.java:66)
	at net.fabricmc.loader.impl.launch.knot.KnotServer.main(KnotServer.java:23)
	at net.fabricmc.loader.impl.launch.server.FabricServerLauncher.main(FabricServerLauncher.java:69)
	... 1 more
Caused by: java.lang.UnsupportedClassVersionError: net/minecraft/bundler/Main has been compiled by a more recent version of the Java Runtime (class file version 69.0), this version of the Java Runtime only recognizes class file versions up to 65.0
	at java.base/java.lang.ClassLoader.defineClass1(Native Method)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at java.base/java.lang.ClassLoader.defineClass(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor$1.loadClass(BundlerProcessor.java:65)
	at java.base/java.lang.ClassLoader.loadClass(Unknown Source)
	at java.base/java.lang.Class.forName0(Native Method)
	at java.base/java.lang.Class.forName(Unknown Source)
	at java.base/java.lang.Class.forName(Unknown Source)
	at net.fabricmc.loader.impl.game.minecraft.BundlerProcessor.process(BundlerProcessor.java:85)
	... 7 more
minecraft.service: Main process exited, code=exited, status=1/FAILURE
minecraft.service: Failed with result 'exit-code'.
minecraft.service: Scheduled restart job, restart counter is at 8.
Stopped Minecraft server.
```