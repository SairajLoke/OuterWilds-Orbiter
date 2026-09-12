# OW-RL
RL agent to control OW ship

#### To build mod and place in owml dir run
.\build.ps1


---------------------------------------

# Todo

### Game Interaction            
1. ship          coords | orientation | vel .....(get orientation facing planet)
1. user          coords | orientation | vel
1. target planet coords | orientation | vel?...
1. force direction 
1. altitude 
1. frame of ref? 
1. on target reached autopilot signal
1. thrustor vals

    
1. controls - map keyboard nums to control velocity & patch it

1. set points :  distance from planet (val | slider) | 

3. speedup - done
1. 
7. Relative motion coords,? are they?
1. visaulize values in ship hud, add a kpi,kpf const controller as well
8. check out orientation ...
9. check pose, velocity vecs
10. distance..
11. make sep class for logging,
12. sep class for pid controller

### RL 
4. figure out reset episode function for RL env
5. figure out {state + obs + reward + actions} 
    - what input to give ( current plan : ship pose(loc+orient) +  loc of planets + masses of planet + acce? )

### add metrics to visualize training (see RL_MAPPED notes)

**Tweaks**: 
1. pSkipFrame 
    as mentioned in original DQN paper  https://arxiv.org/pdf/1312.5602
    "More precisely, the agent sees and selects actions on every kth frame instead of every
    frame, and its last action is repeated on skipped frames. Since running the emulator forward for one
    step requires much less computation than having the agent select an actio"
    
    as ig the state will be very similar as well, so good to skip, but how much that is the question as they menion about Space Invaders.
    Even quad-ai repo uses this

### others
1. make , publish mod to data repo
2. make vid demo
### References
https://owml.outerwildsmods.com/guides/patching.html : on classes used 
https://owml.outerwildsmods.com/guides/patching.html#getting-the-object-youre-patching

https://github.com/ow-mods/outer-wilds-unity-wiki/wiki/Other-%E2%80%90-Global-Messenger-Reference : possibly useful to det events and design rewards/penalties

https://github.com/ow-mods/ow-mod-template

https://owml.outerwildsmods.com/guides/mod_settings.html#getting-values-in-c : mod settings


Useful refs till now
https://owml.outerwildsmods.com/guides/
https://github.com/ow-mods/outer-wilds-unity-wiki/


### handy cmds ---------------------------
dotnet list ShipLogger.csproj package
dir bin\Release\net48\ShipLogger.dll
dotnet clean
dotnet restore --verbosity detailed
dotnet build -c Release 
dotnet build -c Release --verbosity detailed --no-incremental /warnaserror


load mod load, start game ( it stops due to the account stuff)
run the patched outerwilds.exe file
---------------------------------------

