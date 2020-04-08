using System.Collections.Generic;
using Oxide.Core;
using Object = UnityEngine.Object;

namespace Oxide.Plugins
{
    [Info("Backpacks", "Bazz3l", "1.0.0")]
    [Description("Personal backpack storage for players")]
    class Backpacks : RustPlugin
    {
        #region Fields
        const string permUse = "backpacks.use";

        static List<LootController> controllers = new List<LootController>();
        static Backpacks plugin;
        
        BackpackData storage;
        ConfigData config;
        #endregion

        #region Storage
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
            public int Amount;

            public ItemData(string shortname, int amount)
            {
                this.Shortname = shortname;
                this.Amount = amount;
            }
        }

        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storage);
        #endregion

        #region Config
        ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                ContainerCapacity = 6
            };
        }

        class ConfigData
        {
            public int ContainerCapacity;
        }
        #endregion

        #region Oxide
        protected override void LoadDefaultConfig() => Config.WriteObject(GetDefaultConfig(), true);

        void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);

            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
        }

        void Init()
        {
            plugin = this;
            config = Config.ReadObject<ConfigData>();
            storage = Interface.Oxide.DataFileSystem.ReadObject<BackpackData>(Name);
        }

        void Unload()
        {
            foreach(LootController controller in controllers)
            {
                controller?.Destroy();
            }
        }

        void OnNewSave(string filename)
        {
            storage.Players.Clear();
            SaveData();
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
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

            controllers.Remove(controller);
        }

        void OnPlayerLootEnd(PlayerLoot loot)
        {
            BasePlayer player = loot.gameObject.GetComponent<BasePlayer>();
            if (player != loot.entitySource)
            {
                return;
            }

            LootController.Find(player)?.Close();
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
                    capacity    = plugin.config.ContainerCapacity,
                    entityOwner = player,
                    isServer    = true
                };

                Container.GiveUID();
            }

            public static LootController Find(BasePlayer player)
            {
                LootController controller = controllers.Find(x => x.Player.userID == player.userID);
                if (controller == null)
                {
                    controller = new LootController(player);

                    controllers.Add(controller);
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
                List<ItemData> items = plugin.storage.FindItemsByID(userID);

                foreach (ItemData itemData in items)
                {
                    Item item = ItemManager.CreateByName(itemData.Shortname, itemData.Amount);

                    item?.MoveToContainer(container);
                }
            }

            void SaveItems(ulong userID, ItemContainer container)
            {
                List<ItemData> items = plugin.storage.FindItemsByID(userID);

                items.Clear();

                foreach (Item item in container.itemList)
                {
                    items.Add(new ItemData(item.info.shortname, item.amount));
                }

                plugin.SaveData();

                container.Clear();

                ItemManager.DoRemoves();
            }
        }
        #endregion

        #region Commands
        [ChatCommand("backpack")]
        void BackpackCommand(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
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