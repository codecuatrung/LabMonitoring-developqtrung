import { Button, Card, Select, Switch, Table, Typography } from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  DatabaseOutlined,
  PlusOutlined,
  ReloadOutlined,
} from "@ant-design/icons";
import { permissionDefinitions, type RowData } from "./model";
import { AccountDataTable } from "./AccountDataTable";

export type AdminScreenProps = {
  role: string;
  roles: string[];
  users: RowData[];
  permissions: RowData[];
  auditLogs: RowData[];
  canManageUsers: boolean;
  hasPermission: (permission: string) => boolean;
  onRoleChange: (role: string) => void;
  onCreateRole: () => void;
  onAddUser: () => void;
  onEditUser: (user: RowData) => void;
  onRemoveUser: (user: RowData) => void;
  onTogglePermission: (
    role: string,
    permission: string,
    enabled: boolean,
  ) => Promise<void>;
  onResetDemo: () => void;
};

type PermissionRow = {
  id: string;
  permission: string;
  label: string;
  group: string;
};

export function AdminScreen({
  role,
  roles,
  users,
  permissions,
  auditLogs,
  canManageUsers,
  hasPermission,
  onRoleChange,
  onCreateRole,
  onAddUser,
  onEditUser,
  onRemoveUser,
  onTogglePermission,
  onResetDemo,
}: AdminScreenProps) {
  const permissionColumns: ColumnsType<PermissionRow> = [
    { title: "Nhóm", dataIndex: "group", width: 140 },
    { title: "Quyền nghiệp vụ", dataIndex: "label" },
    ...roles.map((targetRole) => ({
      title: targetRole,
      key: targetRole,
      align: "center" as const,
      render: (_: unknown, item: PermissionRow) => {
        const enabled = Boolean(
          permissions.find(
            (row) => row.id === `${targetRole}::${item.permission}`,
          )?.enabled,
        );
        return (
          <Switch
            size="small"
            checked={enabled}
            disabled={
              targetRole === "Quản trị hệ thống" ||
              !hasPermission("users.write")
            }
            onChange={(checked) =>
              onTogglePermission(targetRole, item.permission, checked)
            }
          />
        );
      },
    })),
  ];

  return (
    <>
      <div className="page-toolbar">
        <div>
          <div className="eyebrow">TÀI KHOẢN · VAI TRÒ · QUYỀN NGHIỆP VỤ</div>
          <Typography.Title level={2}>Quản trị hệ thống</Typography.Title>
          <Typography.Text type="secondary">
            Quyền được lưu trong SQLite server và áp dụng ngay vào thao tác
            demo.
          </Typography.Text>
        </div>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          disabled={!canManageUsers}
          onClick={onCreateRole}
        >
          Tạo vai trò
        </Button>
      </div>

      <Card className="panel-card permission-card">
        <div className="role-toolbar">
          <div>
            <strong>Vai trò đang thử</strong>
            <div className="muted">
              Chuyển vai trò để xem menu và quyền thao tác tương ứng.
            </div>
          </div>
          <Select
            value={role}
            onChange={onRoleChange}
            options={roles.map((item) => ({ value: item, label: item }))}
            style={{ width: 230 }}
          />
        </div>
        <Table
          rowKey="permission"
          pagination={false}
          scroll={{ x: 900 }}
          dataSource={permissionDefinitions.map((item) => ({
            ...item,
            id: item.key,
            permission: item.key,
          }))}
          columns={permissionColumns}
        />
      </Card>

      <AccountDataTable
        users={users}
        canManageUsers={canManageUsers}
        onAdd={onAddUser}
        onEdit={onEditUser}
        onRemove={onRemoveUser}
      />

      <Card title="Nhật ký thao tác" className="panel-card">
        <Table
          rowKey="id"
          size="small"
          pagination={{ pageSize: 8 }}
          dataSource={[...auditLogs].sort((first, second) =>
            String(second.occurredAt).localeCompare(String(first.occurredAt)),
          )}
          locale={{ emptyText: "Chưa có thao tác được ghi nhận" }}
          columns={[
            {
              title: "Thời điểm",
              dataIndex: "occurredAt",
              render: (value) =>
                value ? new Date(String(value)).toLocaleString("vi-VN") : "—",
            },
            { title: "Người thực hiện", dataIndex: "actor" },
            { title: "Vai trò", dataIndex: "role" },
            { title: "Thao tác", dataIndex: "action" },
            { title: "Đối tượng", dataIndex: "entity" },
            { title: "Chi tiết", dataIndex: "details" },
          ]}
        />
      </Card>

      <Card className="panel-card reset-card">
        <span className="reset-description">
          <DatabaseOutlined />
          <span>
            <strong>SQLite server RAM · bộ nhớ theo instance</strong>
            <span className="muted">
              Vai trò và quyền được lưu cùng dữ liệu lịch/ca.
            </span>
          </span>
        </span>
        <Button icon={<ReloadOutlined />} onClick={onResetDemo}>
          Khôi phục dữ liệu mẫu
        </Button>
      </Card>
    </>
  );
}
