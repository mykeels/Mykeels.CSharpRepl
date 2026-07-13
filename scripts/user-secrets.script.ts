#!/usr/bin/env bun
/**
 * Manager user-secrets with `dotnet user-secrets`
 *
 * Usage:
 * bun scripts/user-secrets.script.ts
 *
 * Will list options:
 * - add a new user-secret
 * - list all user-secrets keys, allowing you to navigate with the arrow keys
 *   - for each key, you can:
 *     - press enter to preview the value
 *       - when previewing, you can:
 *         -  press c to copy the value to the clipboard
 *         - press e to edit the value
 *     - press delete key to remove the key
 *
 * to list user-secrets, we can use the `dotnet user-secrets list` command
 */

import { execSync } from "child_process";
import prompts from "prompts";
import fs from "fs";
import path from "path";
import fg from "fast-glob";

interface Secret {
  key: string;
  value: string;
}

interface Project {
  path: string;
  name: string;
  userSecretsId: string;
}

let selectedProject: Project | null = null;

// Find all projects with UserSecretsId
function findProjectsWithUserSecrets(): Project[] {
  const csprojFiles = fg.sync("**/*.csproj", {
    ignore: ["**/node_modules/**", "**/bin/**", "**/obj/**"],
  });

  const projects: Project[] = [];

  for (const csprojPath of csprojFiles) {
    try {
      const content = fs.readFileSync(csprojPath, "utf-8");
      const userSecretsIdMatch = content.match(
        /<UserSecretsId>(.*?)<\/UserSecretsId>/,
      );

      if (userSecretsIdMatch) {
        const userSecretsId = userSecretsIdMatch[1].trim();
        const projectName = path.basename(csprojPath, ".csproj");
        const projectDir = path.dirname(csprojPath);

        projects.push({
          path: projectDir,
          name: projectName,
          userSecretsId,
        });
      }
    } catch {
      // Skip files that can't be read
      continue;
    }
  }

  return projects;
}

// Select a project at startup
async function selectProject(): Promise<Project | null> {
  const projects = findProjectsWithUserSecrets();

  if (projects.length === 0) {
    console.error("❌ No projects with UserSecretsId found.");
    return null;
  }

  const response = await prompts({
    type: "select",
    name: "project",
    message: "Select a project to manage secrets:",
    choices: projects.map((project) => ({
      title: `${project.name} (${project.path})`,
      value: project,
      description: `Secrets ID: ${project.userSecretsId}`,
    })),
  });

  return response.project || null;
}

// Get all user secrets
function getUserSecrets(): Secret[] {
  if (!selectedProject) {
    return [];
  }

  try {
    const output = execSync(
      `dotnet user-secrets list --project ${selectedProject.path} --id ${selectedProject.userSecretsId}`,
      { encoding: "utf-8" },
    );

    if (!output.trim()) {
      return [];
    }

    const secrets: Secret[] = [];
    const lines = output.trim().split("\n");

    for (const line of lines) {
      const match = line.match(/^([^=]+)\s*=\s*(.+)$/);
      if (match) {
        secrets.push({
          key: match[1].trim(),
          value: match[2].trim(),
        });
      }
    }

    return secrets;
  } catch (error) {
    console.error("Error fetching user secrets:", error);
    return [];
  }
}

// Get a specific secret value
function getSecretValue(key: string): string | null {
  const secrets = getUserSecrets();
  const secret = secrets.find((s) => s.key === key);
  return secret?.value ?? null;
}

// Set a secret
function setSecret(key: string, value: string): void {
  if (!selectedProject) {
    throw new Error("No project selected");
  }
  execSync(
    `dotnet user-secrets set "${key}" "${value}" --project ${selectedProject.path} --id ${selectedProject.userSecretsId}`,
    { stdio: "inherit" },
  );
}

// Remove a secret
function removeSecret(key: string): void {
  if (!selectedProject) {
    throw new Error("No project selected");
  }
  execSync(
    `dotnet user-secrets remove "${key}" --project ${selectedProject.path} --id ${selectedProject.userSecretsId}`,
    { stdio: "inherit" },
  );
}

// Copy to clipboard (macOS)
function copyToClipboard(text: string): void {
  try {
    execSync(`echo "${text.replace(/"/g, '\\"')}" | pbcopy`);
    console.log("\n✅ Copied to clipboard!");
  } catch (error) {
    console.error("\n❌ Failed to copy to clipboard:", error);
  }
}

// Main menu
async function showMainMenu(): Promise<string | null> {
  const projectInfo = selectedProject ? ` (${selectedProject.name})` : "";
  const response = await prompts({
    type: "select",
    name: "action",
    message: `🔐 User Secrets Manager${projectInfo}`,
    choices: [
      { title: "Add a new secret", value: "add" },
      { title: "List all secrets", value: "list" },
      { title: "Change project", value: "change" },
      { title: "Quit", value: "quit" },
    ],
  });

  if (!response.action) {
    return null; // User cancelled
  }

  return response.action;
}

// Add a new secret
async function addSecret(): Promise<void> {
  const keyResponse = await prompts({
    type: "text",
    name: "key",
    message: "Enter secret key:",
    validate: (value) => (value.trim() ? true : "Key cannot be empty"),
  });

  if (!keyResponse.key) {
    return; // User cancelled
  }

  const valueResponse = await prompts({
    type: "password",
    name: "value",
    message: "Enter secret value:",
    validate: (value) => (value.trim() ? true : "Value cannot be empty"),
  });

  if (!valueResponse.value) {
    return; // User cancelled
  }

  setSecret(keyResponse.key.trim(), valueResponse.value.trim());
  console.log("✅ Secret added!");
}

// List and manage secrets
async function listSecrets(): Promise<void> {
  const secrets = getUserSecrets();

  if (secrets.length === 0) {
    console.log("\n📭 No secrets found.");
    return;
  }

  while (true) {
    // Show list of secrets
    const listResponse = await prompts({
      type: "select",
      name: "secret",
      message: `📋 Secrets (${secrets.length})`,
      choices: [
        ...secrets.map((secret) => ({
          title: secret.key,
          value: secret.key,
        })),
        { title: "← Back to main menu", value: "__back__" },
      ],
    });

    if (!listResponse.secret || listResponse.secret === "__back__") {
      return; // Go back to main menu
    }

    const selectedKey = listResponse.secret;

    // Show actions for selected secret
    const actionResponse = await prompts({
      type: "select",
      name: "action",
      message: `Secret: ${selectedKey}`,
      choices: [
        { title: "👁️  Preview value", value: "preview" },
        { title: "✏️  Edit value", value: "edit" },
        { title: "🗑️  Delete secret", value: "delete" },
        { title: "← Back to list", value: "back" },
      ],
    });

    if (!actionResponse.action || actionResponse.action === "back") {
      continue; // Go back to list
    }

    if (actionResponse.action === "preview") {
      await previewSecret(selectedKey);
    } else if (actionResponse.action === "edit") {
      await editSecret(selectedKey);
      // Refresh secrets list
      const updatedSecrets = getUserSecrets();
      secrets.length = 0;
      secrets.push(...updatedSecrets);
    } else if (actionResponse.action === "delete") {
      const confirmed = await prompts({
        type: "confirm",
        name: "value",
        message: `⚠️  Delete secret "${selectedKey}"?`,
        initial: false,
      });

      if (confirmed.value) {
        removeSecret(selectedKey);
        console.log("✅ Secret removed!");

        // Refresh secrets list
        const updatedSecrets = getUserSecrets();
        secrets.length = 0;
        secrets.push(...updatedSecrets);

        if (secrets.length === 0) {
          console.log("\n📭 No more secrets. Returning to main menu...");
          return;
        }
      }
    }
  }
}

// Preview a secret value
async function previewSecret(key: string): Promise<void> {
  const value = getSecretValue(key);

  if (value === null) {
    console.log("\n❌ Could not retrieve secret value.");
    await prompts({
      type: "text",
      name: "continue",
      message: "Press Enter to continue...",
    });
    return;
  }

  console.log(`\n🔍 Secret: ${key}`);
  console.log("─".repeat(50));
  console.log(value);
  console.log("─".repeat(50));

  const actionResponse = await prompts({
    type: "select",
    name: "action",
    message: "What would you like to do?",
    choices: [
      { title: "📋 Copy to clipboard", value: "copy" },
      { title: "← Back", value: "back" },
    ],
  });

  if (actionResponse.action === "copy") {
    copyToClipboard(value);
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}

// Edit a secret value
async function editSecret(key: string): Promise<void> {
  const currentValue = getSecretValue(key);
  const valueHint = currentValue
    ? ` (current value: ${"*".repeat(Math.min(currentValue.length, 20))}${currentValue.length > 20 ? "..." : ""})`
    : "";

  const response = await prompts({
    type: "password",
    name: "value",
    message: `Enter new value for "${key}"${valueHint}:`,
    validate: (value) => (value.trim() ? true : "Value cannot be empty"),
  });

  if (!response.value) {
    return; // User cancelled
  }

  setSecret(key, response.value.trim());
  console.log("✅ Secret updated!");
}

// Main function
async function main() {
  try {
    // Select project at startup
    selectedProject = await selectProject();
    if (!selectedProject) {
      console.log("\n👋 Goodbye!");
      return;
    }

    console.log(
      `\n✅ Selected project: ${selectedProject.name} (${selectedProject.path})\n`,
    );

    while (true) {
      const choice = await showMainMenu();

      if (!choice || choice === "quit") {
        console.log("\n👋 Goodbye!");
        break;
      }

      if (choice === "add") {
        await addSecret();
      } else if (choice === "list") {
        await listSecrets();
      } else if (choice === "change") {
        selectedProject = await selectProject();
        if (!selectedProject) {
          console.log("\n👋 Goodbye!");
          break;
        }
        console.log(
          `\n✅ Selected project: ${selectedProject.name} (${selectedProject.path})\n`,
        );
      }
    }
  } catch (error) {
    console.error("Error:", error);
    process.exit(1);
  }
}

// Run the script
main().catch((error) => {
  console.error("Fatal error:", error);
  process.exit(1);
});
