# GLB test fixture

SPDX-License-Identifier: CC0-1.0

`TextureCoordinateTest.glb` is copied from the Khronos glTF Sample Assets repository:

- Source model: [Texture Coordinate Test](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/TextureCoordinateTest)
- Download source: [TextureCoordinateTest.glb](https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/main/Models/TextureCoordinateTest/glTF-Binary/TextureCoordinateTest.glb)
- Authored appearance: [Khronos glTF Sample Viewer](https://github.khronos.org/glTF-Sample-Viewer-Release/?model=https%3A%2F%2Fraw.githubusercontent.com%2FKhronosGroup%2FglTF-Sample-Assets%2Fmain%2F.%2FModels%2FTextureCoordinateTest%2FglTF-Binary%2FTextureCoordinateTest.glb)
- Copyright and license: © 2017 Analytical Graphics, Inc.; CC0 1.0 Universal. The Khronos asset catalog credits Ed Mackey for the model. See the repository's [asset catalog](https://github.com/KhronosGroup/glTF-Sample-Assets/blob/main/Models/Models.md) and [REUSE metadata](https://github.com/KhronosGroup/glTF-Sample-Assets/blob/main/REUSE.toml).

The file is a static glTF 2.0 binary with one scene, five identity-transform nodes, and five meshes, each with one indexed primitive using `TRIANGLES` topology. Four primitives contain POSITION, NORMAL, and TEXCOORD_0; the back plane intentionally has no TEXCOORD_0 and is used to check clear unsupported-attribute errors. The authored geometry uses glTF's right-handed, Y-up coordinates with +Z forward. Across all primitives, the expected bounds are approximately X [-1.2, 1.2], Y [-1.2, 1.2], Z [-1, 1], in metres. The first mesh's primitive is a 1 m square in the XY plane near (0.7, 0.7, 0), facing +Z.

The asset and its original license/source are sample content; they are not engine runtime content.

SHA-256: `FE75A63A0423C9A682BF46C3045B6328AE9C55ABEFC9A50ADF3EC1D6DC6EA3B9`
