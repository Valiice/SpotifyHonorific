# SpotifyActivityHonorific

Update honorific title based on your currently playing Spotify track.

## Installation

Installable using my custom repository (instructions here: https://github.com/Valiice/DalamudPluginRepo) or from compiled archives.

## Commands

-   `/spotifyhonorific config`

# Setup

1.  Go to the **Spotify Developer Dashboard** at [developer.spotify.com/dashboard](http://developer.spotify.com/dashboard) and log in.
2.  Click the **"Create App"** button.
3.  Give it a name (e.g., "FFXIV Honorific") and description, then check the "Web API" box.
4.  Find the **"Redirect URIs"** section.
5.  In the text box, type **`http://127.0.0.1:5000/callback`**
6.  Click the **"Add"** button to its right (the red "not secure" warning is normal and can be ignored).
7. Scroll to the bottom and click the **"Save"** button.
8. In FFXIV, open the plugin settings with `/spotifyhonorific config`.
9. Paste your **Client ID** and **Client Secret** into the correct text boxes.
10. Click the **"Authenticate with Spotify"** button.
11. Your web browser will open. Log in to Spotify and grant permission.
12. You're all set! The plugin will now show your currently playing song as your honorific.
