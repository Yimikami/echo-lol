# Echo desktop design

This records the implemented WPF surface, not a finished visual acceptance claim. The user selected concept C in `docs/design/concept-c.png`; corrected UI review remains pending after the user stopped computer control.

## Structure

1600×1000 initial window, 1120×780 minimum. A 78px header contains brand, page title, search and recorder state. A 180px navigation column anchors Library, Matches and Settings. The library uses a virtualized 380px list, a flexible native media player, and a bottom trim workbench. The settings surface is a scrollable form rather than a modal.

## Color and type

Graphite `#15191C`, panel `#1B2124`, warm white `#F1F4F2`, secondary `#ACB9B8`, mint `#9DE1C7`. Death markers use coral; assists use blue. Native Segoe UI at 14px body, 23–28px headings. Primary buttons use dark text on mint. Status is communicated with text as well as color.

## Content and behavior

Only real recordings populate the library. Fixture recordings remain labeled as tests. Empty, offline, failure and export states have explicit copy. Closing the UI releases media and its refresh timer while the recorder remains independent. No browser or animated background is used. Generated game imagery exists only in the labeled design concepts; it is not shipped inside the player.

## Assets and evidence

The original Echo emblem is AI-generated with retained provenance in `assets/brand/provenance.json`. The ICO is a format conversion of that emblem. All generated PNGs retain their prompts. The first desktop inspection identified paused-video and native-control styling issues; source corrections landed afterward. Final corrected screenshots, smaller-window review and full interactive export acceptance are outstanding.
