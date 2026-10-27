# Release

<!-- Followed by the release skill. Change it when the release process changes. -->

Version files: none (the version comes from the tag: release.yml passes -p:Version=${GITHUB_REF_NAME#v})
Changelog: none
Check: dotnet test WinFix.sln
Base: main
Branch: chore/release-X.Y.Z
Merge: auto
Tag: vX.Y.Z
Build: on-tag ckandrinirina/ck-winfix release.yml
Verify: gh release view vX.Y.Z -R ckandrinirina/ck-winfix --json assets -q '.assets[].name'
Assets: WinFix.exe, WinFixUsb-macos-arm64, WinFixUsb-macos-x64
Manual: none
Expected failures: none
