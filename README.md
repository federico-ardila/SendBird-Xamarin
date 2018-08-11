# [SendBird](https://sendbird.com) Xamarin Sample UI

A basic example using SDK Version 3.0.12.0 that connects to an open channel and lets you chat there.
It's a trimmed down version of the previous example that used the 2.X SDK. 
For now onyl Main, SendBirdChannelListActivity and SendBirdChatActivity have been ported, the rest of the example was simply commented out. 
This also shows how to wrap the callbacks oriented SDK into async methods allowing for the use of the await keyword. This has a few advantages:

1. No callback hell
2. Improved readability
3. Exception handling with try catch blocks
4. No need to use SynchronizationContext for UI changes (except in delegate method in the ChannelHandler)

It shouldn't be too dificult to create helper clases to wrap basically the whole SDK into async methods. I might end up doing that just so I don't have to write ad-hoc methods if I create a iOS version of my App.

## Quick Start
0. Create a SendBird Account a user (or 2 if you want to actually test the chat) and and open channel
1. Download Sample UI project from this repository.
2. Set the values for AppId UserId Accesstoken and ChannelUrl in Main
2. Open the project which you want to run.(We currently provide Android only)
3. Build and run it.


If someone shows interest I might clean this up finish it and create a pull request. But for now this was all I needed to learn the SDK and evaluate SendBird for my needs. 

Ok enough talking to myself. 
