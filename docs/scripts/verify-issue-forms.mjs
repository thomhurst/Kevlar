import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { load } from 'js-yaml';

const docsRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const repositoryRoot = path.dirname(docsRoot);
const templatesRoot = path.join(repositoryRoot, '.github', 'ISSUE_TEMPLATE');
const formNames = ['bug_report.yml', 'feature_request.yml', 'question.yml'];
const requiredIds = ['package', 'target-framework', 'version'];
const allowedTypes = new Set(['checkboxes', 'dropdown', 'input', 'markdown', 'textarea']);

function readYaml(fileName) {
  const filePath = path.join(templatesRoot, fileName);
  try {
    return load(fs.readFileSync(filePath, 'utf8'));
  } catch (error) {
    throw new Error(`${fileName}: invalid YAML: ${error.message}`);
  }
}

for (const fileName of formNames) {
  const form = readYaml(fileName);
  if (!form || typeof form.name !== 'string' || typeof form.description !== 'string'
      || !Array.isArray(form.body) || form.body.length === 0) {
    throw new Error(`${fileName}: expected non-empty name, description, and body.`);
  }

  const fields = new Map();
  for (const item of form.body) {
    if (!item || !allowedTypes.has(item.type)) {
      throw new Error(`${fileName}: unsupported issue-form item type '${item?.type}'.`);
    }

    if (item.id) {
      if (fields.has(item.id)) {
        throw new Error(`${fileName}: duplicate field id '${item.id}'.`);
      }
      fields.set(item.id, item);
    }
  }

  for (const id of requiredIds) {
    const field = fields.get(id);
    if (!field || field.validations?.required !== true) {
      throw new Error(`${fileName}: '${id}' must exist and be required.`);
    }
  }

  for (const id of ['package', 'target-framework']) {
    const options = fields.get(id)?.attributes?.options;
    if (!Array.isArray(options) || options.length < 2) {
      throw new Error(`${fileName}: '${id}' must provide dropdown options.`);
    }
  }
}

const config = readYaml('config.yml');
if (config?.blank_issues_enabled !== false || !Array.isArray(config.contact_links)
    || config.contact_links.length === 0) {
  throw new Error('config.yml: blank issues must be disabled and contact_links must be non-empty.');
}

console.log(`Verified ${formNames.length} issue forms and config.yml.`);
