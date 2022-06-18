# GeoTetra AvaUtil

This is essentially a throw bag of utilities for VRChat avatars that I either do not see existing elsewhere, or the other freely available options do it incorrectly.

## Installation

1. Open the Unity Package Manager under `Window > Package Manager`.
2. Click the + in the upper left of package manager window.
3. Select 'Add package from git url...'.
4. Paste `https://github.com/rygo6/GTAvaUtil.git` into the dialogue and clikc 'Add'.

## Usage

All utilities are currently under the `Tools > GeoTetra > GTAvaUtil` menu at the top of the Unity Editor. Currently there are only three utility methods.

### 1 . Transfer SkinnedMeshRenderer Bones...

 This will let you attach a rigged clothing item to the skeleton of your avatar.

There are other utilities that do this but I found they did it incorrectly. You would end up with a duplicated skeleton for each clothing item or it messed up when the bones weren't in the same exact position.

This utility will attach the clothing item to the actual skeleton of the avatar itself, so then all your clothing items will use the same skeleton and GameObjects that the avatar itself uses. 

It also calculates new bindposes in case your avatars bones and your clothing items bones are not in the exact same position.

For this to work your clothing item's skeleton and your avatar's skeleton must have the same hierarchy and naming. It is not necessary that the skeletons exactly match, but every bone present in your clothing items skeleton must also be present in your avatars skeleton. 

*Nearly every clothing package for a particular avatar I have bought off of gumroad has been set up like this, so I assume this should just work for most avatar clothing packages.*

1. First line up your clothing item to your avatar. 
2. Next, select the SkinnedMeshRender you want to transfer bone data from, this is probably your avatars SkinnedMeshRenderer.
3. Next hold down ctrl and select the SkinnedMeshRenderer you want to transfer the bone data onto, this is probably your clothing items SkinnedMeshRender.
4. Now click `Tools > GeoTetra > GTAvaUtil > Transfer SkinnedMeshRenderer Bones...`. If it completes without errors then it worked and should have made a new GameObject next to your avatars SkinnedMeshRenderer with your newly attached clothing item. If you rotate the leg or spine your avatars skeleton, your clothing item should not be properly attached.

### 2. Bake SkinnedMeshRenderer...

This will let you pose a SkinnedMeshRenderer, set its blendshapes and then bake it out to a static mesh. This I mainly use with the package Zologo VertexDirt to then bake down vertex ambient occlusion on a mesh after I tweaked its blendshapes and pose.

1. Pose your skeleton and adjust the blendshapes to what you want.
2. Then select the SkinnedMeshRender and click `Tools > GeoTetra > GTAvaUtil > Bake SkinnedMeshRenderer...`.

### 3. Transfer Mesh Colors...

This will let you transfer the vertex colors from a static mesh onto a SkinnedMeshRenderer. Mainly I use this to transfer baked vertex ambient occlusion onto my avatar.

1. Select the MeshFilter you want to transfer colors from.
2. Select the SkinnedMeshRenderer you want to transfer the colors to.
3. Click `Tools > GeoTetra > GTAvaUtil > Transfer Mesh Colors...`.

### 4. Recalculate SkinnedMeshRenderer Bounds...

This will automatically recalculate the bounds of a SkinnedMeshRenderer.

1. Select SkinnedMeshRenderer.
2. Click `Tools > GeoTetra > GTAvaUtil > Recalculate SkinnedMeshRenderer Bounds...`.
