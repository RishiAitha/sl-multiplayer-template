# Sign Language Multiplayer Template

This is a template project outlining how to set up basic systems for social games involving sign recognition for sign language learning.

## Setting up Unity's Multiplayer Services SDK

Much of the relevant info for creating multiplayer systems can be found here: [Multiplayer Services SDK Docs](https://docs.unity.com/en-us/mps-sdk)
Cost info for multiplayer hosting (which will, at a small scale, almost certainly not become an issue) can be found here: [Unity Services Pricing](https://legacy-activation.unity3d.com/products/gaming-services/pricing)

There are very many approaches to setting up multiplayer games with Unity or other engines. The examples here use a particular approach that is easy to test but still functional when deployed; we use Netcode for GameObjects for basic object networking, send information over a Relay network, and handle match creation with Matchmaker. This allows us to skip cumbersome join codes and other systems. However, feel free to utilize your own alternative approaches! This is not the ONLY way to make a multiplayer mobile game.

Below is very detailed guidance on setting up a project from scratch. When cloning this template, similar steps can be followed, although some has already been done for you.

When creating a project in Unity Hub (ensure you are signed in already), there are two options. You can create a cloud project, or create it locally. By choosing the cloud option, your project will be automatically linked to a Unity Cloud project on your account. You can see it at [Unity Cloud](https://cloud.unity.com/).
![Unity Hub Project Creation](/img/project-creation-hub.png)
![Unity Cloud Project Page](/img/unity-cloud-project-page.png)

If the project isn't linked correctly, or you haven't created a cloud project, you can go into Services under Project Settings and link the project manually. After selecting your organizatio, you'll get to choose between making a new project, or linking to an existing one (set it up on the cloud website if needed).
![Services Settings Page](/img/services-settings.png)

Once the project setup is done, you'll need to install the Multiplayer Services SDK package. It can be found in the unity registry in the package manager, and has the name "com.unity.services.multiplayer".
![Multiplayer Services Package](/img/multiplayer-services-package.png)

Another useful package is the Multiplayer Play Mode package, which allows you to open multiple instances of the project to test multiplayer features without building to multiple devices. The name is "com.unity.multiplayer.playmode".
![Multiplayer Play Mode Package](/img/multiplayer-play-mode-package.png)

For setting up Matchmaker, we need to take a few additional steps.
Under products, enter the matchmaker menu. You should see a step-by-step guide, so start with the "integrate matchmaker" step, and choose Unity and SDK, and hit next until you can finish (the rest is already done). Next, create a queue (the name of the queue will be used to create matches), and choose the max players on a ticket (max group that can queue together, usually 1 unless you have a party system or something). 
Then, you can create a pool, and name it something different and set a timeout (30 or 60 seconds usually). For hosting, select client hosting (for this guide, if you are using a different approach you can choose something else), and then you are taken to the match definition page. Name your match, and choose the number of players per team and the number of teams. You will then be able to use this queue and pool for automatic matchmaking!

## Project Info

The `Assets/General/` Folder contains useful scripts and prefabs for setting up any multiplayer game.
- `MultiplayerManager.cs` handles basic matchmaking and multiplayer tasks, such as initialization, authentication, and session creation.
- `SessionMenu.cs` works with the `Session Menu.prefab` to handle button options and display information relating to the matchmaking process. It works directly with MultiplayerManager.
- `WordBank.cs` holds a list of words used to advance gameplay, and keeps track of progress and other information across the network.

Individual Scenes have more prefabs, assets, and scripts included.
For example, `Assets/RacingGame/` contains the prefabs, assets, and a scene that is a functional SL race car game!
These scenes use the existing generic tools to create a full game loop.
- Do take note of scripts like `RaceCar.cs`. These are the NetworkBehaviour scripts that run on Netcode player prefabs.
    - This one instantiates cars in different positions and with different sprites based on the player order, then handles movement across clients.
    - These scripts are the ones you will most heavily need to modify to accomodate whatever custom game logic you have. (you can edit the others still, of course)

### SLRTk

You will need to get access and import the package for SLRTk independently.