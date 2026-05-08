import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import net from "net";

const UNITY_PORT = 6400;
const UNITY_HOST = "127.0.0.1";

function sendToUnity(command) {
  return new Promise((resolve, reject) => {
    const client = new net.Socket();
    let data = "";

    client.setTimeout(12000);
    client.connect(UNITY_PORT, UNITY_HOST, () => {
      client.write(JSON.stringify(command) + "\n");
    });
    client.on("data", (chunk) => { data += chunk.toString(); });
    client.on("end", () => {
      try {
        resolve(JSON.parse(data.trim()));
      } catch {
        reject(new Error("Invalid JSON from Unity: " + data));
      }
    });
    client.on("timeout", () => {
      client.destroy();
      reject(new Error("Unity connection timed out. Is Unity Editor open?"));
    });
    client.on("error", (e) => {
      reject(new Error(`Cannot reach Unity (port ${UNITY_PORT}): ${e.message}. Open Unity Editor first.`));
    });
  });
}

async function call(command) {
  const res = await sendToUnity(command);
  if (!res.success) throw new Error(res.error);
  return res.result;
}

const server = new McpServer({ name: "unity-mcp", version: "1.0.0" });

server.tool(
  "unity_ping",
  "Unity Editorが起動しているか確認する",
  {},
  async () => {
    const result = await call({ type: "ping" });
    return { content: [{ type: "text", text: result }] };
  }
);

server.tool(
  "unity_get_project_info",
  "Unityプロジェクトの基本情報を取得する（製品名・Unityバージョン・プラットフォーム等）",
  {},
  async () => {
    const result = await call({ type: "get_project_info" });
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
  }
);

server.tool(
  "unity_get_scene_hierarchy",
  "現在開いているUnityシーンのGameObject階層を取得する",
  {},
  async () => {
    const result = await call({ type: "get_scene_hierarchy" });
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
  }
);

server.tool(
  "unity_get_logs",
  "Unityコンソールのログを取得する",
  {
    count: z.number().int().min(1).max(500).optional().describe("取得するログ件数（デフォルト50）"),
  },
  async ({ count }) => {
    const result = await call({ type: "get_logs", count: count ?? 50 });
    return { content: [{ type: "text", text: result.join("\n") }] };
  }
);

server.tool(
  "unity_get_assets",
  "Unityプロジェクト内のアセット一覧を取得する",
  {
    filter: z.string().optional().describe(
      "検索フィルター（例: 't:Script', 't:Prefab', 'Assets/Characters t:Texture2D'）"
    ),
  },
  async ({ filter }) => {
    const result = await call({ type: "get_assets", filter: filter ?? "" });
    return { content: [{ type: "text", text: result.join("\n") }] };
  }
);

server.tool(
  "unity_execute_menu_item",
  "Unity EditorのメニューアイテムをC#コードなしで実行する",
  {
    path: z.string().describe("メニューパス（例: 'Assets/Refresh', 'Edit/Play'）"),
  },
  async ({ path }) => {
    const result = await call({ type: "execute_menu_item", path });
    return { content: [{ type: "text", text: result }] };
  }
);

server.tool(
  "unity_refresh_assets",
  "UnityのAssetDatabaseを更新する（ファイルをディスクから再読み込みする）",
  {},
  async () => {
    const result = await call({ type: "refresh_assets" });
    return { content: [{ type: "text", text: result }] };
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
