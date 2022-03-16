using AnkrSDK.Core.Infrastructure;
using AnkrSDK.Core.Data;
using AnkrSDK.Core.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace AnkrSDK.Examples.UseCases.AddSwitchNetwork
{
	public class AddSwitchNetwork : MonoBehaviour
	{
		[SerializeField]
		private Button _bscButton;

		[SerializeField]
		private Button _bscTestButton;

		private void Awake()
		{
			_bscButton.onClick.AddListener(OpenAddSwitchBsc);
			_bscTestButton.onClick.AddListener(OpenAddSwitchBscTestnet);
		}

		private void OnDestroy()
		{
			_bscButton.onClick.RemoveListener(OpenAddSwitchBsc);
			_bscTestButton.onClick.RemoveListener(OpenAddSwitchBscTestnet);
		}

		private static void OpenAddSwitchBsc()
		{
			AnkrNetworkHelper.AddAndSwitchNetwork(NetworkName.BinanceSmartChain);
		}

		private static void OpenAddSwitchBscTestnet()
		{
			AnkrNetworkHelper.AddAndSwitchNetwork(NetworkName.BinanceSmartChainTestNet);
		}
	}
}