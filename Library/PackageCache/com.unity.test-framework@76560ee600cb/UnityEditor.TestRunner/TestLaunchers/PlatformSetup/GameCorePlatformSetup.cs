using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor.Build;

#if UNITY_GAMECORE
using UnityEditor.GameCore;
using UnityEditor.TestRunner.CommandLineParser;
#endif

namespace UnityEditor.TestTools.TestRunner
{
    internal class GameCorePlatformSetup : IPlatformSetup
    {
#if UNITY_GAMECORE
        private GameCoreDeployMethod gameCoreDeploymentMethod = GameCoreDeployMethod.Push;
        private const string k_PlsSize = "512";
        private const string k_PlsGrow = "1024";
        private const string k_TitleID = "73ECA03C";
        private const string k_MSAAppId = "0000000040283475";
        private const string k_SCID = "00000000-0000-0000-0000-000073ECA03C";

        private const string k_GameCorePackageDeployment = "GameCoreDeploymentMethod";
        private static GameCoreSettings GetGameCoreSettings()
        {
#if UNITY_GAMECORE_XBOXSERIES
            return GameCoreScarlettSettings.GetInstance();
#elif UNITY_GAMECORE_XBOXONE
            return GameCoreXboxOneSettings.GetInstance();
#endif
        }

        private Dictionary<string, object> GetTestSettings()
        {
            var commandLineArgs = Environment.GetCommandLineArgs();
            var testSettingsFilePath = string.Empty;
            var optionSet = new CommandLineOptionSet(new CommandLineOption("testSettingsFile", settingsFilePath => { testSettingsFilePath = settingsFilePath; }));
            optionSet.Parse(commandLineArgs);
            Dictionary<string, object> settingsDictionary = null;
            if (!string.IsNullOrEmpty(testSettingsFilePath))
            {
                var text = File.ReadAllText(testSettingsFilePath);
                settingsDictionary = Json.Deserialize(text) as Dictionary<string, object>;
            }

            return settingsDictionary;
        }

        private void TrySetDeploymentMethod(Dictionary<string, object> settingsDictionary)
        {
            if (settingsDictionary.ContainsKey(k_GameCorePackageDeployment))
            {
                settingsDictionary.TryGetValue(k_GameCorePackageDeployment, out var argumentValue);
                gameCoreDeploymentMethod = (GameCoreDeployMethod)Enum.Parse(typeof(GameCoreDeployMethod), argumentValue.ToString());
            }
        }
#endif

        public void Setup()
        {
#if UNITY_GAMECORE
            var settingsDictionary = GetTestSettings();
			if (settingsDictionary != null)
			{
				TrySetDeploymentMethod(settingsDictionary);
			}
			
			GameCoreSettings settings = GetGameCoreSettings();
			
            if (settings == null)
                return;

            settings.DeploymentMethod = gameCoreDeploymentMethod;

            settings.SCID = k_SCID;
            settings.GameConfig.MSAAppId = k_MSAAppId;
            settings.GameConfig.TitleId = k_TitleID;

            settings.GameConfig.PersistentLocalStorage = new GameCore.GameConfig.CT_PersistentLocalStorage();
            settings.GameConfig.PersistentLocalStorage.SizeMB = k_PlsSize;
            settings.GameConfig.PersistentLocalStorage.GrowableToMB = k_PlsGrow;
            settings.SaveGameConfig();
#endif
        }

        public void PostBuildAction()
        {
        }

        public void PostSuccessfulBuildAction()
        {
        }

        public void PostSuccessfulLaunchAction()
        {
        }

        public void CleanUp()
        {

        }
    }
}
