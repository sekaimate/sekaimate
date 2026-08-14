#nullable enable

using System;
using System.Text;
using Basis.Contrib.Crypto;

namespace Basis.Network.Core
{
	/// X25519 + HKDF-SHA256 key agreement for the encrypted peer-to-peer (direct) link.
	/// The two peers exchange ephemeral public keys through the server's signalling
	/// channel and each derives the same two directional keys, because ECDH(myPriv,
	/// peerPub) is symmetric; the transcript (both public keys) is folded into the HKDF
	/// salt for channel binding.
	public static class BasisCryptoHandshake
	{
		public const int PublicKeySize = BasisX25519.KeySize;
		public const int PrivateKeySize = BasisX25519.KeySize;
		public const int KeySize = BasisAeadCipher.KeySize;

		private static readonly byte[] InfoAB = Encoding.ASCII.GetBytes("basis-crypto-v1-ab");
		private static readonly byte[] InfoBA = Encoding.ASCII.GetBytes("basis-crypto-v1-ba");
		private static readonly byte[] InfoClientToServer = Encoding.ASCII.GetBytes("basis-sso-v1-client-to-server");
		private static readonly byte[] InfoServerToClient = Encoding.ASCII.GetBytes("basis-sso-v1-server-to-client");

		public static void GenerateKeyPair(out byte[] privateKey, out byte[] publicKey)
			=> BasisX25519.GenerateKeyPair(out privateKey, out publicKey);

		/// Derives the directional keys for a peer-to-peer link. Role is decided by
		/// public-key ordering so both ends agree without extra signalling.
		public static bool DerivePeerKeys(
			ReadOnlySpan<byte> myPrivate,
			ReadOnlySpan<byte> myPublic,
			ReadOnlySpan<byte> peerPublic,
			out byte[] sendKey,
			out byte[] recvKey)
		{
			sendKey = Array.Empty<byte>();
			recvKey = Array.Empty<byte>();
			try
			{
				int cmp = Compare(myPublic, peerPublic);
				if (cmp == 0) return false;
				bool iAmA = cmp < 0;

				ReadOnlySpan<byte> aPub = iAmA ? myPublic : peerPublic;
				ReadOnlySpan<byte> bPub = iAmA ? peerPublic : myPublic;

				byte[] shared = BasisX25519.Agree(myPrivate, peerPublic);
				byte[] salt = Concat(aPub, bPub);
				byte[] keyAB = BasisHkdf.DeriveKey(shared, salt, InfoAB, KeySize);
				byte[] keyBA = BasisHkdf.DeriveKey(shared, salt, InfoBA, KeySize);

				if (iAmA)
				{
					sendKey = keyAB;
					recvKey = keyBA;
				}
				else
				{
					sendKey = keyBA;
					recvKey = keyAB;
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Derives keys for the SSO pre-auth channel. The client pins the server's long-lived public
		/// key, while it generates a new ephemeral X25519 keypair for every join attempt.
		/// </summary>
		public static bool DeriveSsoClientKeys(
			ReadOnlySpan<byte> clientPrivate, ReadOnlySpan<byte> clientPublic, ReadOnlySpan<byte> serverPublic,
			out byte[] sendKey, out byte[] recvKey)
			=> DeriveSsoKeys(clientPrivate, clientPublic, serverPublic, true, out sendKey, out recvKey);

		public static bool DeriveSsoServerKeys(
			ReadOnlySpan<byte> serverPrivate, ReadOnlySpan<byte> clientPublic, ReadOnlySpan<byte> serverPublic,
			out byte[] sendKey, out byte[] recvKey)
			=> DeriveSsoKeys(serverPrivate, clientPublic, serverPublic, false, out sendKey, out recvKey);

		private static bool DeriveSsoKeys(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> clientPublic,
			ReadOnlySpan<byte> serverPublic, bool client, out byte[] sendKey, out byte[] recvKey)
		{
			sendKey = Array.Empty<byte>(); recvKey = Array.Empty<byte>();
			try
			{
				byte[] shared = BasisX25519.Agree(privateKey, client ? serverPublic : clientPublic);
				byte[] salt = Concat(clientPublic, serverPublic);
				byte[] clientToServer = BasisHkdf.DeriveKey(shared, salt, InfoClientToServer, KeySize);
				byte[] serverToClient = BasisHkdf.DeriveKey(shared, salt, InfoServerToClient, KeySize);
				sendKey = client ? clientToServer : serverToClient;
				recvKey = client ? serverToClient : clientToServer;
				return true;
			}
			catch { return false; }
		}

		private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			var result = new byte[a.Length + b.Length];
			a.CopyTo(result);
			b.CopyTo(result.AsSpan(a.Length));
			return result;
		}

		private static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			int n = Math.Min(a.Length, b.Length);
			for (int i = 0; i < n; i++)
			{
				int d = a[i] - b[i];
				if (d != 0) return d;
			}
			return a.Length - b.Length;
		}
	}
}
