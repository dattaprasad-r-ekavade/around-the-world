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

## Rigged Fox animation fixture

`Fox.glb` is the Khronos glTF Sample Asset “Fox,” downloaded from the repository's
`Models/Fox/glTF-Binary` directory. Its README describes a rigged fox with `Survey`, `Walk`,
and `Run` cycles. The source viewer link displays the bundled GLB: [Fox in the Khronos glTF
Sample Viewer](https://github.khronos.org/glTF-Sample-Viewer-Release/?model=https%3A%2F%2Fraw.githubusercontent.com%2FKhronosGroup%2FglTF-Sample-Assets%2Fmain%2FModels%2FFox%2FglTF-Binary%2FFox.glb).

The original model by PixelMannen is CC0. Rigging and animation by tomkranis, and glTF
conversion by AsoboStudio and scurest, are CC BY 4.0. Attribution: “Fox model by PixelMannen;
rigging and animation by tomkranis; glTF conversion by AsoboStudio and scurest,” from the
[Khronos glTF Sample Assets](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox).
The rigging, animation, and conversion are used under [Creative Commons Attribution 4.0
International](https://creativecommons.org/licenses/by/4.0/). The fixture is unmodified.

The default scene has two roots (`root` and `fox`). The skinned `fox` node uses skin 0; its
skeleton root is `_rootJoint`, with 24 joints in the listed skin order and 24 inverse-bind
matrices. The first joint's rest transform and inverse-bind matrix are identity. The root chain
continues `_rootJoint` → `b_Root_00` → `b_Hip_01` → `b_Spine01_02` → `b_Spine02_03` →
`b_Neck_04` → `b_Head_05`; the hip also branches to both arms, tail, and legs. Clip durations
read from the asset are Survey 3.416667 s, Walk 0.708333 s, and Run 1.158333 s. Tests pin the
joint count, representative rest-pose hierarchy, and those durations.

SHA-256: `D97044E701822BAC5A62696459B27D7B375AADA5DE8574ED4362EDBBA94771F7`
