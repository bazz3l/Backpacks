using System.Collections.Generic;
using Oxide.Core;
using Object = UnityEngine.Object;

namespace Oxide.Plugins
{
    [Info("Backpacks", "Bazz3l", "1.0.3")]
    [Description("Allows player to store items in an hidden backpack")]
    class Backpacks : RustPlugin
    {
        #region Fields
        const string _permUse = "backpacks.use";

        List<LootController> _controllers = new List<LootController>();
        BackpackData _backpackData;
        PluginConfig _config;

        public static Backpacks Instance;
        #endregion

        #region stored
        class BackpackData
        {
            public Dictionary<ulong, List<ItemData>> Players = new Dictionary<ulong, List<ItemData>>();

            public List<ItemData> FindItemsByID(ulong userID)
            {
                List<ItemData> items;

                if (!Players.TryGetValue(userID, out items))
                {
                    items = Players[userID] = new List<ItemData>();
                }

                return items;
            }
        }

        class ItemData
        {
            public string Shortname;
            public ulong Skin;
            public int Amount;

            public ItemData(string shortname, ulong skin, int amount)
            {
                Shortname = shortname;
                Skin = skin;
                Amount = amount;
            }
        }

        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _backpackData);
        #endregion

        #region Config
        PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                ContainerCapacity = 6
            };
        }

        class PluginConfig
        {
            public int ContainerCapacity;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        void OnServerInitialized()
        {
            permission.RegisterPermission(_permUse, this);

            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
        }

        void Init()
        {
            Instance = this;
            _config = Config.ReadObject<PluginConfig>();
            _backpackData = Interface.Oxide.DataFileSystem.ReadObject<BackpackData>(Name);
        }

        void Unload()
        {
            foreach(LootController controller in _controllers)
            {
                controller?.Destroy();
            }
        }

        void OnNewSave(string filename)
        {
            _backpackData.Players.Clear();
            SaveData();
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, _permUse))
            {
                return;
            }

            LootController.Find(player);
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            LootController controller = LootController.Find(player);
            if (controller == null)
            {
                return;
            }

            controller?.Destroy();

            _controllers.Remove(controller);
        }

        void OnPlayerLootEnd(PlayerLoot loot)
        {
            BasePlayer player = loot.gameObject.GetComponent<BasePlayer>();
            if (player != loot.entitySource)
            {
                return;
            }

            LootController controller = LootController.Find(player);
            if (controller == null)
            {
                return;
            }

            controller?.Destroy();

            _controllers.Remove(controller);
        }

        object CanLootPlayer(BasePlayer looter, Object target)
        {
            if (looter != target)
            {
                return null;
            }

            LootController container = LootController.Find(looter);
            if (!container.IsOpen)
            {
                return null;
            }

            return true;
        }
        #endregion

        #region Core
        class LootController
        {
            public ItemContainer Container;
            public BasePlayer Player;
            public bool IsOpen;

            public LootController(BasePlayer player)
            {
                Player = player;

                Container = new ItemContainer {
                    allowedContents = ItemContainer.ContentsType.Generic,
                    capacity = Instance._config.ContainerCapacity,
                    entityOwner = player,
                    isServer = true
                };

                Container.GiveUID();
            }

            public static LootController Find(BasePlayer player)
            {
                LootController controller = Instance._controllers.Find(x => x.Player.userID == player.userID);
                if (controller == null)
                {
                    controller = new LootController(player);

                    Instance._controllers.Add(controller);
                }

                return controller;
            }

            public void Open()
            {
                IsOpen = true;

                PlayerLoot loot = Player.inventory.loot;
                loot.Clear();
                loot.PositionChecks = false;
                loot.entitySource   = Player;
                loot.itemSource     = null;
                loot.AddContainer(Container);
                loot.SendImmediate();

                RestoreItems(Player.userID, Container);

                Player.ClientRPCPlayer(null, Player, "RPC_OpenLootPanel", "generic");
            }

            public void Close()
            {
                IsOpen = false;

                SaveItems(Player.userID, Container);
            }

            public void Destroy()
            {
                Close();

                Container?.Kill();
            }

            void RestoreItems(ulong userID, ItemContainer container)
            {
                List<ItemData> items = Instance._backpackData.FindItemsByID(userID);

                foreach (ItemData itemData in items)
                {
                    Item item = ItemManager.CreateByName(itemData.Shortname, itemData.Amount, itemData.Skin);

                    item?.MoveToContainer(container);
                }
            }

            void SaveItems(ulong userID, ItemContainer container)
            {
                List<ItemData> items = Instance._backpackData.FindItemsByID(userID);

                items.Clear();

                foreach (Item item in container.itemList)
                {
                    items.Add(new ItemData(item.info.shortname, item.skin, item.amount));
                }

                Instance.SaveData();

                container.Clear();

                ItemManager.DoRemoves();
            }
        }
        #endregion

        #region Commands
        [ChatCommand("backpack")]
        void BackpackCommand(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, _permUse))
            {
                return;
            }

            timer.Once(0.5f, () => LootController.Find(player)?.Open());
        }
        #endregion

        #region Helpers
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        #endregion
    }
}