# Linking Account Wallet

This is an example for one of the use cases for this sdk : the linking of a crypto wallet to a player.

## 1. Initialize Web3

To start this use case we need to call web3.Initialize method after login in metamask

```c#
string provider_url = "<ethereum node or infura url>";
		
Web3 web3 = new Web3(provider_url);
web3.Initialize();
```

Once Initialized you can then send a signature request like :

```c#
string message = "Hi i am a message !"
string signature = await web3.Sign(message);
```

web3.Sign(string) will return the result of the signature process. If the result is sucessfull it will return the User's adress. 
You can then save that adress in your database as a means of authentication.