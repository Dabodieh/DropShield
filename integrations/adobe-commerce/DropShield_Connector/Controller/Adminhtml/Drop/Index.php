<?php

declare(strict_types=1);

namespace DropShield\Connector\Controller\Adminhtml\Drop;

use Magento\Backend\App\Action;
use Magento\Framework\Controller\ResultFactory;

class Index extends Action
{
    public const ADMIN_RESOURCE = 'DropShield_Connector::protected_drops';

    public function execute()
    {
        $page = $this->resultFactory->create(ResultFactory::TYPE_PAGE);
        $page->setActiveMenu(self::ADMIN_RESOURCE);
        $page->getConfig()->getTitle()->prepend(__('Protected Drops'));
        return $page;
    }
}
